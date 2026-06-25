using MergeDuo.Cards.Domain.Abstractions;
using MergeDuo.Cards.Domain.Documents;
using MergeDuo.Cards.Domain.Exceptions;

namespace MergeDuo.Cards.Tests.Fakes;

public sealed class InMemoryCardsRepository : ICardsRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<(string UserId, string Id), CardDocument> _cards = [];

    public void Seed(CardDocument card)
    {
        lock (_gate)
        {
            card.ETag ??= NewEtag();
            _cards[(card.UserId, card.Id)] = Clone(card);
        }
    }

    public CardDocument? Stored(string userId, string cardId)
    {
        lock (_gate)
        {
            return _cards.TryGetValue((userId, cardId), out var card) ? Clone(card) : null;
        }
    }

    public Task<IReadOnlyList<CardDocument>> ListActiveAsync(string userId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<CardDocument> result = _cards.Values
                .Where(x => x.UserId == userId && x.DeletedAt is null)
                .OrderByDescending(x => x.CreatedAt)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<CardDocument?> GetByIdAsync(
        string userId,
        string cardId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_cards.TryGetValue((userId, cardId), out var card))
            {
                return Task.FromResult<CardDocument?>(null);
            }

            if (!includeDeleted && card.DeletedAt is not null)
            {
                return Task.FromResult<CardDocument?>(null);
            }

            return Task.FromResult<CardDocument?>(Clone(card));
        }
    }

    public Task CreateAsync(CardDocument card, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = (card.UserId, card.Id);
            if (_cards.ContainsKey(key))
            {
                throw new CardsConflictException("card_conflict", "Card conflict.");
            }

            card.ETag = NewEtag();
            _cards[key] = Clone(card);
            return Task.CompletedTask;
        }
    }

    public Task<CardDocument> PatchAsync(
        CardDocument card,
        CardPatch patch,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_cards.TryGetValue((card.UserId, card.Id), out var stored) || stored.DeletedAt is not null)
            {
                throw new CardsNotFoundException("card_not_found", "Card not found.");
            }

            EnsureEtag(stored, ifMatchEtag, clientProvidedEtag);

            stored.Title = patch.Title ?? stored.Title;
            stored.ClosingDay = patch.ClosingDay ?? stored.ClosingDay;
            stored.DueDay = patch.DueDay ?? stored.DueDay;
            stored.Currency = patch.Currency ?? stored.Currency;
            stored.UpdatedAt = patch.UpdatedAt;
            stored.ETag = NewEtag();
            return Task.FromResult(Clone(stored));
        }
    }

    public Task SoftDeleteAsync(
        CardDocument card,
        DateTimeOffset deletedAt,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_cards.TryGetValue((card.UserId, card.Id), out var stored) || stored.DeletedAt is not null)
            {
                throw new CardsNotFoundException("card_not_found", "Card not found.");
            }

            EnsureEtag(stored, ifMatchEtag, clientProvidedEtag);

            stored.DeletedAt = deletedAt;
            stored.UpdatedAt = deletedAt;
            stored.ETag = NewEtag();
            return Task.CompletedTask;
        }
    }

    private static void EnsureEtag(CardDocument stored, string ifMatchEtag, bool clientProvidedEtag)
    {
        if (!string.Equals(stored.ETag, ifMatchEtag, StringComparison.Ordinal))
        {
            throw clientProvidedEtag
                ? new CardsPreconditionFailedException("precondition_failed", "Card precondition failed.")
                : new CardsConflictException("card_conflict", "Card conflicted.");
        }
    }

    private static string NewEtag() => Guid.NewGuid().ToString("N");

    private static CardDocument Clone(CardDocument card) =>
        new()
        {
            Id = card.Id,
            DocType = card.DocType,
            SchemaVersion = card.SchemaVersion,
            UserId = card.UserId,
            Title = card.Title,
            ClosingDay = card.ClosingDay,
            DueDay = card.DueDay,
            Currency = card.Currency,
            CreatedAt = card.CreatedAt,
            UpdatedAt = card.UpdatedAt,
            DeletedAt = card.DeletedAt,
            ETag = card.ETag
        };
}
