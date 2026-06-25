using MergeDuo.Cards.Domain.Abstractions;
using MergeDuo.Cards.Domain.Contracts;
using MergeDuo.Cards.Domain.Exceptions;

namespace MergeDuo.Cards.Tests.Fakes;

public sealed class FakeUsageClient : ICardUsageClient
{
    private readonly Dictionary<(string UserId, string CardId, string YearMonth), CardUsageTotals> _items = [];

    public List<(string UserId, string CardId, string YearMonth, string? Authorization)> Calls { get; } = [];
    public bool Fail { get; set; }

    public void Seed(string userId, CardUsageTotals totals) =>
        _items[(userId, totals.CardId, totals.YearMonth)] = totals;

    public Task<CardUsageTotals> GetUsageAsync(
        string userId,
        string cardId,
        string yearMonth,
        string? authorizationHeader,
        CancellationToken cancellationToken)
    {
        Calls.Add((userId, cardId, yearMonth, authorizationHeader));
        if (Fail)
        {
            throw new CardsDependencyException("cards_dependency_unavailable", "Transactions Service unavailable.");
        }

        return Task.FromResult(
            _items.TryGetValue((userId, cardId, yearMonth), out var totals)
                ? totals
                : new CardUsageTotals(cardId, yearMonth, "BRL", 0m, 0, 0));
    }
}
