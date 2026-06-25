using MergeDuo.Transactions.Domain.Abstractions;
using MergeDuo.Transactions.Domain.Contracts;
using MergeDuo.Transactions.Domain.Documents;
using MergeDuo.Transactions.Domain.Exceptions;
using MergeDuo.Transactions.Domain.Options;
using MergeDuo.Transactions.Domain.Rules;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MergeDuo.Transactions.Domain.Services;

public interface ITransactionsService
{
    Task<TransactionsListResponse> ListAsync(
        string userId,
        string yearMonth,
        string? category,
        string? cardId,
        string? owner,
        int? pageSize,
        string? continuationToken,
        string? sort,
        CancellationToken cancellationToken);

    Task<TransactionResponse> GetAsync(string currentUserId, string transactionId, string yearMonth, string? ownerUserId, CancellationToken cancellationToken);
    Task<CreateTransactionsResponse> CreateAsync(string userId, CreateTransactionRequest request, string? idempotencyKey, CancellationToken cancellationToken);
    Task<TransactionResponse> PatchAsync(string userId, string transactionId, string yearMonth, UpdateTransactionRequest request, string? ifMatchEtag, CancellationToken cancellationToken);
    Task SoftDeleteAsync(string userId, string transactionId, string yearMonth, string? ifMatchEtag, CancellationToken cancellationToken);
    Task<TransactionGroupResponse> GetGroupAsync(string currentUserId, string groupId, string? ownerUserId, CancellationToken cancellationToken);
    Task<DeleteTransactionGroupResponse> DeleteGroupAsync(string userId, string groupId, CancellationToken cancellationToken);
    Task<CardUsageResponse> GetCardUsageAsync(string userId, string cardId, string yearMonth, CancellationToken cancellationToken);
    Task<TagAnalyticsResponse> GetTagAnalyticsAsync(string userId, bool includeTransactions, CancellationToken cancellationToken);
    Task<TagSuggestionsResponse> GetTagSuggestionsAsync(string userId, string? prefix, int? limit, CancellationToken cancellationToken);
}

public sealed class TransactionsService(
    ITransactionsRepository transactions,
    ICardsReadRepository cards,
    IFixedRulesReadRepository fixedRules,
    IPartnershipsReadRepository partnerships,
    ITransactionIdGenerator idGenerator,
    TimeProvider clock,
    TransactionsOptions options) : ITransactionsService
{
    public async Task<TransactionsListResponse> ListAsync(
        string userId,
        string yearMonth,
        string? category,
        string? cardId,
        string? owner,
        int? pageSize,
        string? continuationToken,
        string? sort,
        CancellationToken cancellationToken)
    {
        EnsureUser(userId);
        var validYearMonth = YearMonthRules.EnsureValid(yearMonth);
        var filter = ParseOwner(owner);
        var validPageSize = Math.Clamp(pageSize ?? options.DefaultPageSize, 1, options.MaxPageSize);
        var sortDirection = ParseSort(sort);
        var tokenState = ContinuationTokenState.Decode(continuationToken, options.ContinuationTokenSecret);
        var filters = new TransactionListFilters(
            string.IsNullOrWhiteSpace(category) ? null : CategoryRules.EnsureValid(category),
            string.IsNullOrWhiteSpace(cardId) ? null : EnsureCardId(cardId));

        if (filter == OwnerFilter.Me)
        {
            var page = await transactions.ListMonthAsync(userId, validYearMonth, filters, validPageSize, tokenState?.MeToken, sortDirection, cancellationToken);
            return new TransactionsListResponse(
                page.Items.Select(t => TransactionMapping.ToResponse(t)).ToArray(),
                ContinuationTokenState.Encode(page.ContinuationToken, null, options.ContinuationTokenSecret));
        }

        var partner = await partnerships.GetActivePartnerAsync(userId, cancellationToken);
        if (partner is null)
        {
            return filter == OwnerFilter.Partner
                ? new TransactionsListResponse([], null)
                : await ListAsync(userId, validYearMonth, category, cardId, "me", pageSize, continuationToken, sort, cancellationToken);
        }

        if (filter == OwnerFilter.Partner)
        {
            var page = await transactions.ListMonthAsync(partner.PartnerUserId, validYearMonth, filters, validPageSize, tokenState?.PartnerToken, sortDirection, cancellationToken);
            var cardTitles = await ResolveCardTitlesAsync(partner.PartnerUserId, page.Items, cancellationToken);
            return new TransactionsListResponse(
                page.Items.Select(t => TransactionMapping.ToResponse(t, t.CardId is not null ? cardTitles.GetValueOrDefault(t.CardId) : null)).ToArray(),
                ContinuationTokenState.Encode(null, page.ContinuationToken, options.ContinuationTokenSecret));
        }

        var ownPage = await transactions.ListMonthAsync(userId, validYearMonth, filters, validPageSize, tokenState?.MeToken, sortDirection, cancellationToken);
        var partnerPage = await transactions.ListMonthAsync(partner.PartnerUserId, validYearMonth, filters, validPageSize, tokenState?.PartnerToken, sortDirection, cancellationToken);
        var partnerCardTitles = await ResolveCardTitlesAsync(partner.PartnerUserId, partnerPage.Items, cancellationToken);
        var merged = ownPage.Items.Select(t => TransactionMapping.ToResponse(t))
            .Concat(partnerPage.Items.Select(t => TransactionMapping.ToResponse(t, t.CardId is not null ? partnerCardTitles.GetValueOrDefault(t.CardId) : null)));
        merged = sortDirection == SortDirection.DateAsc
            ? merged.OrderBy(x => x.Date).ThenBy(x => x.CreatedAt)
            : merged.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt);

        return new TransactionsListResponse(
            merged.ToArray(),
            ContinuationTokenState.Encode(ownPage.ContinuationToken, partnerPage.ContinuationToken, options.ContinuationTokenSecret));
    }

    public async Task<TransactionResponse> GetAsync(
        string currentUserId,
        string transactionId,
        string yearMonth,
        string? ownerUserId,
        CancellationToken cancellationToken)
    {
        EnsureUser(currentUserId);
        if (!TransactionIdRules.IsValid(transactionId))
        {
            throw new TransactionsBadRequestException("invalid_transaction_id", "Invalid transaction id.");
        }

        var validYearMonth = YearMonthRules.EnsureValid(yearMonth);
        var targetUserId = string.IsNullOrWhiteSpace(ownerUserId) ? currentUserId : ownerUserId.Trim();
        if (targetUserId != currentUserId && !await partnerships.IsActivePartnerAsync(currentUserId, targetUserId, cancellationToken))
        {
            throw new TransactionsNotFoundException("transaction_not_found", "Transaction not found.");
        }

        var transaction = await transactions.GetByIdAsync(targetUserId, validYearMonth, transactionId, includeDeleted: false, cancellationToken);
        if (transaction is null)
        {
            throw new TransactionsNotFoundException("transaction_not_found", "Transaction not found.");
        }

        if (targetUserId == currentUserId)
        {
            return TransactionMapping.ToResponse(transaction);
        }

        var cardTitles = await ResolveCardTitlesAsync(targetUserId, [transaction], cancellationToken);
        return TransactionMapping.ToResponse(transaction, ResolvedCardTitle(transaction, cardTitles));
    }

    public async Task<CreateTransactionsResponse> CreateAsync(
        string userId,
        CreateTransactionRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureUser(userId);
        EnsureNoExtraFields(request.ExtraFields);
        var category = CategoryRules.EnsureValid(request.Category);
        var total = InstallmentRules.EnsureTotal(request.Installments?.Total, options.MaxInstallments);
        var amount = MoneyRules.EnsureValid(request.Amount);
        var amounts = InstallmentRules.SplitAmount(amount, total);
        var groupId = total > 1 ? NewGroupId(userId, idempotencyKey, request) : null;
        var now = clock.GetUtcNow();
        var documents = new List<TransactionDocument>(total);

        for (var i = 1; i <= total; i++)
        {
            documents.Add(await BuildCreateDocumentAsync(
                userId,
                request,
                category,
                amounts[i - 1],
                total,
                i,
                groupId,
                idempotencyKey,
                now,
                cancellationToken));
        }

        foreach (var document in documents)
        {
            try
            {
                await transactions.CreateAsync(document, cancellationToken);
            }
            catch (TransactionsConflictException) when (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var existing = await transactions.GetByIdAsync(document.UserId, document.YearMonth, document.Id, includeDeleted: false, cancellationToken);
                if (existing is null || !EquivalentForIdempotency(existing, document))
                {
                    throw new TransactionsConflictException("idempotency_key_reused", "Idempotency key reused with different payload.");
                }
            }
        }

        return new CreateTransactionsResponse(groupId, documents.Select(t => TransactionMapping.ToResponse(t)).ToArray());
    }

    public async Task<TransactionResponse> PatchAsync(
        string userId,
        string transactionId,
        string yearMonth,
        UpdateTransactionRequest request,
        string? ifMatchEtag,
        CancellationToken cancellationToken)
    {
        EnsureUser(userId);
        EnsureNoExtraFields(request.ExtraFields);
        if (!TransactionIdRules.IsValid(transactionId))
        {
            throw new TransactionsBadRequestException("invalid_transaction_id", "Invalid transaction id.");
        }

        var validYearMonth = YearMonthRules.EnsureValid(yearMonth);
        var current = await transactions.GetByIdAsync(userId, validYearMonth, transactionId, includeDeleted: false, cancellationToken)
            ?? throw new TransactionsNotFoundException("transaction_not_found", "Transaction not found.");

        if (current.Installments is not null)
        {
            throw new TransactionsBadRequestException("invalid_installments", "Installment groups must be deleted and recreated in v1.");
        }

        var updated = await BuildPatchDocumentAsync(userId, current, request, cancellationToken);
        var etag = SelectEtag(current, ifMatchEtag);

        if (updated.YearMonth == current.YearMonth)
        {
            var patch = new TransactionPatch(
                updated.Category,
                updated.Kind,
                updated.Date,
                updated.PurchaseDate,
                updated.Description,
                updated.Amount,
                updated.Currency,
                updated.OwnerLabel,
                updated.CardId,
                updated.FixedRuleId,
                updated.Tags,
                updated.Notes,
                updated.UpdatedAt);
            var patched = await transactions.PatchAsync(current, patch, etag, !string.IsNullOrWhiteSpace(ifMatchEtag), cancellationToken);
            return TransactionMapping.ToResponse(patched);
        }

        await transactions.CreateMovedCopyAsync(updated, cancellationToken);
        try
        {
            await transactions.SoftDeleteAsync(current, updated.UpdatedAt, etag, !string.IsNullOrWhiteSpace(ifMatchEtag), cancellationToken);
        }
        catch
        {
            try
            {
                await transactions.SoftDeleteAsync(updated, updated.UpdatedAt, updated.ETag ?? "*", clientProvidedEtag: false, cancellationToken);
            }
            catch
            {
            }

            throw;
        }

        return TransactionMapping.ToResponse(updated);
    }

    public async Task SoftDeleteAsync(string userId, string transactionId, string yearMonth, string? ifMatchEtag, CancellationToken cancellationToken)
    {
        EnsureUser(userId);
        if (!TransactionIdRules.IsValid(transactionId))
        {
            throw new TransactionsBadRequestException("invalid_transaction_id", "Invalid transaction id.");
        }

        var validYearMonth = YearMonthRules.EnsureValid(yearMonth);
        var current = await transactions.GetByIdAsync(userId, validYearMonth, transactionId, includeDeleted: false, cancellationToken)
            ?? throw new TransactionsNotFoundException("transaction_not_found", "Transaction not found.");
        await transactions.SoftDeleteAsync(current, clock.GetUtcNow(), SelectEtag(current, ifMatchEtag), !string.IsNullOrWhiteSpace(ifMatchEtag), cancellationToken);
    }

    public async Task<TransactionGroupResponse> GetGroupAsync(string currentUserId, string groupId, string? ownerUserId, CancellationToken cancellationToken)
    {
        EnsureUser(currentUserId);
        if (!GroupIdRules.IsValid(groupId))
        {
            throw new TransactionsBadRequestException("invalid_group_id", "Invalid group id.");
        }

        var targetUserId = string.IsNullOrWhiteSpace(ownerUserId) ? currentUserId : ownerUserId.Trim();
        if (targetUserId != currentUserId && !await partnerships.IsActivePartnerAsync(currentUserId, targetUserId, cancellationToken))
        {
            return new TransactionGroupResponse(groupId, []);
        }

        var items = await transactions.ListGroupAsync(targetUserId, groupId, cancellationToken);
        if (targetUserId == currentUserId)
        {
            return new TransactionGroupResponse(groupId, items.Select(t => TransactionMapping.ToResponse(t)).ToArray());
        }

        var cardTitles = await ResolveCardTitlesAsync(targetUserId, items, cancellationToken);
        return new TransactionGroupResponse(
            groupId,
            items.Select(t => TransactionMapping.ToResponse(t, ResolvedCardTitle(t, cardTitles))).ToArray());
    }

    public async Task<DeleteTransactionGroupResponse> DeleteGroupAsync(string userId, string groupId, CancellationToken cancellationToken)
    {
        EnsureUser(userId);
        if (!GroupIdRules.IsValid(groupId))
        {
            throw new TransactionsBadRequestException("invalid_group_id", "Invalid group id.");
        }

        var items = await transactions.ListGroupAsync(userId, groupId, cancellationToken);
        if (items.Count == 0)
        {
            throw new TransactionsNotFoundException("transaction_group_not_found", "Transaction group not found.");
        }

        var deleted = 0;
        foreach (var item in items)
        {
            await transactions.SoftDeleteAsync(item, clock.GetUtcNow(), item.ETag ?? "*", clientProvidedEtag: false, cancellationToken);
            deleted++;
        }

        return new DeleteTransactionGroupResponse(groupId, deleted, 0);
    }

    public async Task<CardUsageResponse> GetCardUsageAsync(string userId, string cardId, string yearMonth, CancellationToken cancellationToken)
    {
        EnsureUser(userId);
        var validYearMonth = YearMonthRules.EnsureValid(yearMonth);
        var card = await LoadCardAsync(userId, cardId, cancellationToken);
        var totals = await transactions.GetCardUsageAsync(userId, validYearMonth, card.Id, cancellationToken);
        return new CardUsageResponse(card.Id, validYearMonth, totals.Currency, totals.TotalAmount, totals.TransactionCount, totals.InstallmentCount);
    }

    public async Task<TagAnalyticsResponse> GetTagAnalyticsAsync(
        string userId,
        bool includeTransactions,
        CancellationToken cancellationToken)
    {
        EnsureUser(userId);

        var userIds = new List<string> { userId };
        var partner = await partnerships.GetActivePartnerAsync(userId, cancellationToken);
        if (partner is not null)
        {
            userIds.Add(partner.PartnerUserId);
        }

        var knownTags = new HashSet<string>(StringComparer.Ordinal);
        var transactionsByTag = new Dictionary<string, List<TransactionDocument>>(StringComparer.Ordinal);

        foreach (var targetUserId in userIds.Distinct(StringComparer.Ordinal))
        {
            var taggedTransactions = await transactions.ListTaggedAsync(targetUserId, includeTransactions, cancellationToken);
            foreach (var transaction in taggedTransactions)
            {
                foreach (var tag in NormalizedTags(transaction.Tags))
                {
                    knownTags.Add(tag);
                    if (!transactionsByTag.TryGetValue(tag, out var taggedItems))
                    {
                        taggedItems = [];
                        transactionsByTag[tag] = taggedItems;
                    }

                    taggedItems.Add(transaction);
                }
            }

            var fixedRuleTags = await fixedRules.ListActiveTagsAsync(targetUserId, cancellationToken);
            foreach (var tag in NormalizedTags(fixedRuleTags))
            {
                knownTags.Add(tag);
            }
        }

        Dictionary<string, string?>? partnerCardTitles = null;
        if (includeTransactions && partner is not null)
        {
            partnerCardTitles = await ResolveCardTitlesAsync(
                partner.PartnerUserId,
                transactionsByTag.Values.SelectMany(x => x).Where(x => x.UserId == partner.PartnerUserId),
                cancellationToken);
        }

        var summaries = knownTags
            .Select(tag =>
            {
                var taggedTransactions = transactionsByTag.TryGetValue(tag, out var items)
                    ? includeTransactions
                        ? items
                            .OrderByDescending(x => x.Date)
                            .ThenByDescending(x => x.CreatedAt)
                            .ToArray()
                        : items.ToArray()
                    : Array.Empty<TransactionDocument>();
                var expensesTotal = taggedTransactions
                    .Where(x => string.Equals(x.Kind, "out", StringComparison.Ordinal))
                    .Sum(x => x.Amount);

                return new TagSummary(
                    tag,
                    expensesTotal,
                    taggedTransactions.Length,
                    includeTransactions
                        ? taggedTransactions
                            .Select(t => TransactionMapping.ToResponse(
                                t,
                                t.UserId == partner?.PartnerUserId && partnerCardTitles is not null
                                    ? ResolvedCardTitle(t, partnerCardTitles)
                                    : null))
                            .ToArray()
                        : null);
            })
            .OrderByDescending(x => x.ExpensesTotal)
            .ThenByDescending(x => x.TransactionCount)
            .ThenBy(x => x.Tag, StringComparer.Ordinal)
            .ToArray();

        return new TagAnalyticsResponse(summaries.Select(x => x.Tag).ToArray(), summaries);
    }

    public async Task<TagSuggestionsResponse> GetTagSuggestionsAsync(
        string userId,
        string? prefix,
        int? limit,
        CancellationToken cancellationToken)
    {
        EnsureUser(userId);

        var normalizedPrefix = (prefix ?? "").Trim().ToLowerInvariant();
        var take = Math.Clamp(
            limit ?? options.TagSuggestionsDefaultLimit,
            1,
            options.TagSuggestionsMaxLimit);

        var userIds = new List<string> { userId };
        var partner = await partnerships.GetActivePartnerAsync(userId, cancellationToken);
        if (partner is not null)
        {
            userIds.Add(partner.PartnerUserId);
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var targetUserId in userIds.Distinct(StringComparer.Ordinal))
        {
            var taggedTransactions = await transactions.ListTaggedAsync(targetUserId, false, cancellationToken);
            foreach (var transaction in taggedTransactions)
            {
                foreach (var tag in NormalizedTags(transaction.Tags))
                {
                    counts[tag] = counts.TryGetValue(tag, out var current) ? current + 1 : 1;
                }
            }

            var fixedRuleTags = await fixedRules.ListActiveTagsAsync(targetUserId, cancellationToken);
            foreach (var tag in NormalizedTags(fixedRuleTags))
            {
                if (!counts.ContainsKey(tag))
                {
                    counts[tag] = 0;
                }
            }
        }

        var items = counts
            .Where(x => normalizedPrefix.Length == 0
                || x.Key.StartsWith(normalizedPrefix, StringComparison.Ordinal))
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Take(take)
            .Select(x => new TagSuggestion(x.Key, x.Value))
            .ToArray();

        return new TagSuggestionsResponse(items);
    }

    private async Task<TransactionDocument> BuildCreateDocumentAsync(
        string userId,
        CreateTransactionRequest request,
        string category,
        decimal amount,
        int total,
        int index,
        string? groupId,
        string? idempotencyKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalized = await NormalizeTransactionFieldsAsync(userId, request, category, amount, index, cancellationToken);
        var payloadHash = PayloadHash.For(request);

        return new TransactionDocument
        {
            Id = string.IsNullOrWhiteSpace(idempotencyKey)
                ? idGenerator.NewTransactionId()
                : idGenerator.FromIdempotencyKey("tx", userId, idempotencyKey, payloadHash, index),
            UserId = userId,
            YearMonth = YearMonthRules.FromDate(normalized.Date),
            Date = normalized.Date,
            PurchaseDate = normalized.PurchaseDate,
            Category = category,
            Kind = CategoryRules.KindFor(category),
            Description = TextRules.Description(request.Description),
            Amount = amount,
            Currency = CurrencyRules.Normalize(request.Currency),
            OwnerLabel = TextRules.OwnerLabel(request.OwnerLabel),
            CardId = normalized.CardId,
            FixedRuleId = normalized.FixedRuleId,
            Installments = total > 1 ? new InstallmentDocument { Index = index, Total = total, GroupId = groupId! } : null,
            Tags = TextRules.Tags(request.Tags),
            Notes = TextRules.Notes(request.Notes),
            Source = new TransactionSourceDocument { Channel = normalized.FixedRuleId is null ? "manual" : "fixed_rule" },
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
    }

    private async Task<TransactionDocument> BuildPatchDocumentAsync(
        string userId,
        TransactionDocument current,
        UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        if (NoPatchFields(request))
        {
            throw new TransactionsBadRequestException("empty_patch", "Patch must include at least one editable field.");
        }

        var category = request.Category is null ? current.Category : CategoryRules.EnsureValid(request.Category);
        var amount = request.Amount is null ? current.Amount : MoneyRules.EnsureValid(request.Amount);
        var pseudoRequest = new CreateTransactionRequest
        {
            Date = category == CategoryRules.CreditCard ? request.Date : request.Date ?? current.Date,
            PurchaseDate = category == CategoryRules.CreditCard ? request.PurchaseDate ?? current.PurchaseDate : request.PurchaseDate,
            Category = category,
            Description = request.Description ?? current.Description,
            Amount = amount,
            Currency = request.Currency ?? current.Currency,
            OwnerLabel = request.OwnerLabel ?? current.OwnerLabel,
            CardId = category == CategoryRules.CreditCard ? request.CardId ?? current.CardId : request.CardId,
            FixedRuleId = request.FixedRuleId ?? current.FixedRuleId,
            Tags = request.Tags ?? current.Tags,
            Notes = request.Notes ?? current.Notes
        };
        var normalized = await NormalizeTransactionFieldsAsync(userId, pseudoRequest, category, amount, installmentIndex: 1, cancellationToken);
        var now = clock.GetUtcNow();

        return new TransactionDocument
        {
            Id = current.Id,
            UserId = userId,
            YearMonth = YearMonthRules.FromDate(normalized.Date),
            Date = normalized.Date,
            PurchaseDate = normalized.PurchaseDate,
            Category = category,
            Kind = CategoryRules.KindFor(category),
            Description = TextRules.Description(pseudoRequest.Description),
            Amount = amount,
            Currency = CurrencyRules.Normalize(pseudoRequest.Currency),
            OwnerLabel = TextRules.OwnerLabel(pseudoRequest.OwnerLabel),
            CardId = normalized.CardId,
            FixedRuleId = normalized.FixedRuleId,
            Installments = null,
            Tags = TextRules.Tags(pseudoRequest.Tags),
            Notes = TextRules.Notes(pseudoRequest.Notes),
            Source = new TransactionSourceDocument { Channel = normalized.FixedRuleId is null ? "manual" : "fixed_rule" },
            CreatedAt = current.CreatedAt,
            UpdatedAt = now,
            DeletedAt = null
        };
    }

    private async Task<NormalizedFields> NormalizeTransactionFieldsAsync(
        string userId,
        CreateTransactionRequest request,
        string category,
        decimal amount,
        int installmentIndex,
        CancellationToken cancellationToken)
    {
        _ = amount;
        string? cardId = null;
        DateOnly date;
        DateOnly? purchaseDate = null;

        if (category == CategoryRules.CreditCard)
        {
            if (request.Date is not null)
            {
                throw new TransactionsBadRequestException("invalid_date", "Credit card transactions must not send date.");
            }

            if (request.PurchaseDate is null)
            {
                throw new TransactionsBadRequestException("invalid_date", "Credit card transactions require purchaseDate.");
            }

            var card = await LoadCardAsync(userId, request.CardId, cancellationToken);
            cardId = card.Id;
            purchaseDate = request.PurchaseDate.Value;
            date = CardInvoiceRules.DueDateForPurchase(card, purchaseDate.Value, installmentIndex);
        }
        else
        {
            if (request.Date is null)
            {
                throw new TransactionsBadRequestException("invalid_date", "Transaction date is required.");
            }

            if (request.PurchaseDate is not null)
            {
                throw new TransactionsBadRequestException("invalid_date", "purchaseDate is only valid for credit_card.");
            }

            if (!string.IsNullOrWhiteSpace(request.CardId))
            {
                throw new TransactionsBadRequestException("invalid_card_id", "cardId is only valid for credit_card.");
            }

            date = request.Date.Value;
        }

        var fixedRuleId = await NormalizeFixedRuleAsync(userId, request.FixedRuleId, category, cardId, cancellationToken);
        return new NormalizedFields(date, purchaseDate, cardId, fixedRuleId);
    }

    private async Task<string?> NormalizeFixedRuleAsync(
        string userId,
        string? fixedRuleId,
        string category,
        string? cardId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fixedRuleId))
        {
            return null;
        }

        if (!FixedRuleIdRules.IsValid(fixedRuleId))
        {
            throw new TransactionsBadRequestException("invalid_fixed_rule_id", "Invalid fixed rule id.");
        }

        var rule = await fixedRules.GetActiveAsync(userId, fixedRuleId, cancellationToken);
        if (rule is null || !rule.Active)
        {
            throw new TransactionsBadRequestException("invalid_fixed_rule_id", "Invalid fixed rule id.");
        }

        if (!string.Equals(rule.Category, category, StringComparison.Ordinal))
        {
            throw new TransactionsBadRequestException("invalid_fixed_rule_id", "Fixed rule category is incompatible.");
        }

        if (rule.CardId is not null && !string.Equals(rule.CardId, cardId, StringComparison.Ordinal))
        {
            throw new TransactionsBadRequestException("invalid_fixed_rule_id", "Fixed rule card is incompatible.");
        }

        return rule.Id;
    }

    private async Task<CardDocument> LoadCardAsync(string userId, string? cardId, CancellationToken cancellationToken)
    {
        if (!CardIdRules.IsValid(cardId))
        {
            throw new TransactionsBadRequestException("invalid_card_id", "Invalid card id.");
        }

        return await cards.GetActiveAsync(userId, cardId!, cancellationToken)
            ?? throw new TransactionsBadRequestException("invalid_card_id", "Invalid card id.");
    }

    private async Task<Dictionary<string, string?>> ResolveCardTitlesAsync(
        string userId,
        IEnumerable<TransactionDocument> txs,
        CancellationToken cancellationToken)
    {
        var cardIds = txs
            .Where(t => t.Category == "credit_card" && t.CardId is not null)
            .Select(t => t.CardId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var cid in cardIds)
        {
            var card = await cards.GetActiveAsync(userId, cid, cancellationToken);
            result[cid] = card?.Title;
        }
        return result;
    }

    private static string? ResolvedCardTitle(TransactionDocument transaction, Dictionary<string, string?> cardTitles) =>
        transaction.CardId is not null && cardTitles.TryGetValue(transaction.CardId, out var title)
            ? title
            : null;

    private string? NewGroupId(string userId, string? idempotencyKey, CreateTransactionRequest request) =>
        string.IsNullOrWhiteSpace(idempotencyKey)
            ? idGenerator.NewGroupId()
            : idGenerator.FromIdempotencyKey("txg", userId, idempotencyKey, PayloadHash.For(request), 0);

    private static void EnsureUser(string userId)
    {
        if (!UserIdRules.IsValid(userId))
        {
            throw new TransactionsBadRequestException("unauthorized", "JWT is missing, invalid or expired.");
        }
    }

    private static string EnsureCardId(string cardId)
    {
        if (!CardIdRules.IsValid(cardId))
        {
            throw new TransactionsBadRequestException("invalid_card_id", "Invalid card id.");
        }

        return cardId;
    }

    private static void EnsureNoExtraFields(Dictionary<string, System.Text.Json.JsonElement>? extraFields)
    {
        if (extraFields is { Count: > 0 })
        {
            throw new TransactionsBadRequestException("invalid_request", "Request contains unsupported fields.");
        }
    }

    private static OwnerFilter ParseOwner(string? owner) => (owner ?? "me").Trim().ToLowerInvariant() switch
    {
        "me" => OwnerFilter.Me,
        "partner" => OwnerFilter.Partner,
        "both" => OwnerFilter.Both,
        _ => throw new TransactionsBadRequestException("invalid_request", "Invalid owner filter.")
    };

    private static SortDirection ParseSort(string? sort) => (sort ?? "dateAsc").Trim().ToLowerInvariant() switch
    {
        "dateasc" => SortDirection.DateAsc,
        "datedesc" => SortDirection.DateDesc,
        _ => throw new TransactionsBadRequestException("invalid_request", "Invalid sort.")
    };

    private static string SelectEtag(TransactionDocument transaction, string? ifMatchEtag)
    {
        if (string.IsNullOrWhiteSpace(ifMatchEtag))
        {
            throw new TransactionsPreconditionFailedException("if_match_required", "If-Match header is required for transaction updates.");
        }

        return ifMatchEtag.Trim();
    }

    private static bool NoPatchFields(UpdateTransactionRequest request) =>
        request.Date is null
        && request.PurchaseDate is null
        && request.Category is null
        && request.Description is null
        && request.Amount is null
        && request.Currency is null
        && request.OwnerLabel is null
        && request.CardId is null
        && request.FixedRuleId is null
        && request.Tags is null
        && request.Notes is null;

    private static bool EquivalentForIdempotency(TransactionDocument left, TransactionDocument right) =>
        left.UserId == right.UserId
        && left.YearMonth == right.YearMonth
        && left.Date == right.Date
        && left.PurchaseDate == right.PurchaseDate
        && left.Category == right.Category
        && left.Kind == right.Kind
        && left.Description == right.Description
        && left.Amount == right.Amount
        && left.Currency == right.Currency
        && left.CardId == right.CardId
        && left.FixedRuleId == right.FixedRuleId
        && left.Installments?.GroupId == right.Installments?.GroupId
        && left.Installments?.Index == right.Installments?.Index
        && left.Installments?.Total == right.Installments?.Total
        && (left.Tags ?? []).SequenceEqual(right.Tags ?? [], StringComparer.Ordinal);

    private static IEnumerable<string> NormalizedTags(IEnumerable<string>? values)
    {
        if (values is null)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var tag = (value ?? "").Trim().ToLowerInvariant();
            if (tag.Length > 0 && seen.Add(tag))
            {
                yield return tag;
            }
        }
    }

    private sealed record NormalizedFields(DateOnly Date, DateOnly? PurchaseDate, string? CardId, string? FixedRuleId);

    private sealed record ContinuationTokenState(string? MeToken, string? PartnerToken, string Signature)
    {
        public static string? Encode(string? meToken, string? partnerToken, string secret)
        {
            if (string.IsNullOrWhiteSpace(meToken) && string.IsNullOrWhiteSpace(partnerToken))
            {
                return null;
            }

            var signature = Sign(meToken, partnerToken, secret);
            var json = JsonSerializer.Serialize(new ContinuationTokenState(meToken, partnerToken, signature));
            return Base64Url(Encoding.UTF8.GetBytes(json));
        }

        public static ContinuationTokenState? Decode(string? token, string secret)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            try
            {
                var json = Encoding.UTF8.GetString(Base64UrlDecode(token));
                var state = JsonSerializer.Deserialize<ContinuationTokenState>(json);
                if (state is null || state.Signature != Sign(state.MeToken, state.PartnerToken, secret))
                {
                    throw new TransactionsBadRequestException("invalid_request", "Invalid continuation token.");
                }

                return state;
            }
            catch (TransactionsBadRequestException)
            {
                throw;
            }
            catch
            {
                throw new TransactionsBadRequestException("invalid_request", "Invalid continuation token.");
            }
        }

        private static string Sign(string? meToken, string? partnerToken, string secret)
        {
            var key = Encoding.UTF8.GetBytes(secret);
            var payload = Encoding.UTF8.GetBytes($"{meToken ?? ""}|{partnerToken ?? ""}");
            return Base64Url(HMACSHA256.HashData(key, payload));
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] Base64UrlDecode(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            return Convert.FromBase64String(padded);
        }
    }
}
