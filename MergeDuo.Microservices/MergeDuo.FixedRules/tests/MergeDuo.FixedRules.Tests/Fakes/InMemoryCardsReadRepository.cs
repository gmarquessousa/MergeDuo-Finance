using MergeDuo.FixedRules.Domain.Abstractions;
using MergeDuo.FixedRules.Domain.Documents;

namespace MergeDuo.FixedRules.Tests.Fakes;

public sealed class InMemoryCardsReadRepository : ICardsReadRepository
{
    private readonly Dictionary<(string UserId, string Id), CardProjection> _cards = [];

    public void Seed(CardProjection card) => _cards[(card.UserId, card.Id)] = card;

    public Task<CardProjection?> GetActiveCardAsync(string userId, string cardId, CancellationToken cancellationToken)
    {
        var found = _cards.TryGetValue((userId, cardId), out var card) && card.DeletedAt is null
            ? new CardProjection { Id = card.Id, UserId = card.UserId, DeletedAt = card.DeletedAt }
            : null;
        return Task.FromResult(found);
    }
}
