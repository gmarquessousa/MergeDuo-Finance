using MergeDuo.Cards.Domain.Abstractions;
using MergeDuo.Cards.Domain.Contracts;
using MergeDuo.Cards.Domain.Documents;
using MergeDuo.Cards.Domain.Exceptions;
using MergeDuo.Cards.Domain.Rules;

namespace MergeDuo.Cards.Domain.Services;

public interface ICardsService
{
    Task<CardsListResponse> ListAsync(string userId, CancellationToken cancellationToken);
    Task<CardResponse> GetAsync(string userId, string cardId, CancellationToken cancellationToken);
    Task<CardResponse> CreateAsync(string userId, CreateCardRequest request, CancellationToken cancellationToken);
    Task<CardResponse> PatchAsync(string userId, string cardId, UpdateCardRequest request, string? ifMatchEtag, CancellationToken cancellationToken);
    Task SoftDeleteAsync(string userId, string cardId, string? ifMatchEtag, CancellationToken cancellationToken);
    Task<CardUsageResponse> GetUsageAsync(
        string userId,
        string cardId,
        string yearMonth,
        string? authorizationHeader,
        CancellationToken cancellationToken);
}

public sealed class CardsService(
    ICardsRepository cards,
    ICardUsageClient usageClient,
    ICardIdGenerator idGenerator,
    TimeProvider clock) : ICardsService
{
    public async Task<CardsListResponse> ListAsync(string userId, CancellationToken cancellationToken)
    {
        EnsureUserId(userId);
        var documents = await cards.ListActiveAsync(userId, cancellationToken);
        return new CardsListResponse(documents.Select(CardMapping.ToResponse).ToArray());
    }

    public async Task<CardResponse> GetAsync(string userId, string cardId, CancellationToken cancellationToken)
    {
        var card = await GetActiveCardAsync(userId, cardId, cancellationToken);
        return CardMapping.ToResponse(card);
    }

    public async Task<CardResponse> CreateAsync(string userId, CreateCardRequest request, CancellationToken cancellationToken)
    {
        EnsureUserId(userId);
        EnsureNoExtraFields(request.ExtraFields);

        var now = clock.GetUtcNow();
        var title = TitleRules.Normalize(request.Title);
        var closingDay = BillingDayRules.EnsureValid(request.ClosingDay);
        var dueDay = BillingDayRules.EnsureValid(request.DueDay);
        var currency = CurrencyCodeRules.Normalize(request.Currency);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var card = new CardDocument
            {
                Id = idGenerator.NewId(),
                DocType = "card",
                SchemaVersion = 1,
                UserId = userId,
                Title = title,
                ClosingDay = closingDay,
                DueDay = dueDay,
                Currency = currency,
                CreatedAt = now,
                UpdatedAt = now,
                DeletedAt = null
            };

            try
            {
                await cards.CreateAsync(card, cancellationToken);
                return CardMapping.ToResponse(card);
            }
            catch (CardsConflictException) when (attempt < 2)
            {
            }
        }

        throw new CardsConflictException("card_conflict", "Card id conflict.");
    }

    public async Task<CardResponse> PatchAsync(
        string userId,
        string cardId,
        UpdateCardRequest request,
        string? ifMatchEtag,
        CancellationToken cancellationToken)
    {
        EnsureNoExtraFields(request.ExtraFields);
        var card = await GetActiveCardAsync(userId, cardId, cancellationToken);
        var patch = new CardPatch(
            request.Title is null ? null : TitleRules.Normalize(request.Title),
            request.ClosingDay is null ? null : BillingDayRules.EnsureValid(request.ClosingDay),
            request.DueDay is null ? null : BillingDayRules.EnsureValid(request.DueDay),
            request.Currency is null ? null : CurrencyCodeRules.Normalize(request.Currency),
            clock.GetUtcNow());

        if (!patch.HasChanges)
        {
            throw new CardsBadRequestException("empty_patch", "Patch must include at least one editable field.");
        }

        var etag = SelectEtag(card, ifMatchEtag);
        var updated = await cards.PatchAsync(card, patch, etag, !string.IsNullOrWhiteSpace(ifMatchEtag), cancellationToken);
        return CardMapping.ToResponse(updated);
    }

    public async Task SoftDeleteAsync(string userId, string cardId, string? ifMatchEtag, CancellationToken cancellationToken)
    {
        var card = await GetActiveCardAsync(userId, cardId, cancellationToken);
        var etag = SelectEtag(card, ifMatchEtag);
        await cards.SoftDeleteAsync(card, clock.GetUtcNow(), etag, !string.IsNullOrWhiteSpace(ifMatchEtag), cancellationToken);
    }

    public async Task<CardUsageResponse> GetUsageAsync(
        string userId,
        string cardId,
        string yearMonth,
        string? authorizationHeader,
        CancellationToken cancellationToken)
    {
        var validYearMonth = YearMonthRules.EnsureValid(yearMonth);
        var card = await GetActiveCardAsync(userId, cardId, cancellationToken);
        var cycle = BillingCycleRules.Calculate(card.ClosingDay, card.DueDay, validYearMonth);
        var totals = await usageClient.GetUsageAsync(userId, card.Id, validYearMonth, authorizationHeader, cancellationToken);

        return new CardUsageResponse(
            card.Id,
            validYearMonth,
            totals.Currency,
            totals.TotalAmount,
            totals.TransactionCount,
            totals.InstallmentCount,
            CardMapping.ToResponse(cycle),
            "transactions-service",
            new CardUsageFreshnessResponse(
                clock.GetUtcNow(),
                "transactions-service",
                "fresh",
                true,
                false));
    }

    private async Task<CardDocument> GetActiveCardAsync(string userId, string cardId, CancellationToken cancellationToken)
    {
        EnsureUserId(userId);
        if (!CardIdRules.IsValid(cardId))
        {
            throw new CardsBadRequestException("invalid_card_id", "Invalid card id.");
        }

        var card = await cards.GetByIdAsync(userId, cardId, includeDeleted: false, cancellationToken);
        return card ?? throw new CardsNotFoundException("card_not_found", "Card not found.");
    }

    private static void EnsureUserId(string userId)
    {
        if (!UserIdRules.IsValid(userId))
        {
            throw new CardsBadRequestException("unauthorized", "JWT is missing, invalid or expired.");
        }
    }

    private static string SelectEtag(CardDocument card, string? ifMatchEtag)
    {
        if (string.IsNullOrWhiteSpace(ifMatchEtag))
        {
            throw new CardsPreconditionFailedException("if_match_required", "If-Match header is required for card updates.");
        }

        return ifMatchEtag.Trim();
    }

    private static void EnsureNoExtraFields(Dictionary<string, System.Text.Json.JsonElement>? extraFields)
    {
        if (extraFields is { Count: > 0 })
        {
            throw new CardsBadRequestException("invalid_request", "Request contains unsupported fields.");
        }
    }
}
