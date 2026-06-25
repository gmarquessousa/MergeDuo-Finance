using MergeDuo.Aggregates.Domain.Abstractions;
using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Rules;
using MergeDuo.Aggregates.Domain.Services;

namespace MergeDuo.Aggregates.Tests.Fakes;

public sealed class FakeReadinessProbe : IAggregatesReadinessProbe
{
    public bool Ready { get; set; } = true;
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(Ready);
}

public sealed class InMemoryMonthlyAggregatesRepository : IMonthlyAggregatesRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<(string UserId, string YearMonth), MonthlyAggregateDocument> _items = [];

    public int UpsertCount { get; private set; }

    public void Seed(MonthlyAggregateDocument document)
    {
        lock (_gate)
        {
            _items[(document.UserId, document.YearMonth)] = Clone(document);
        }
    }

    public MonthlyAggregateDocument? Stored(string userId, YearMonth yearMonth)
    {
        lock (_gate)
        {
            return _items.TryGetValue((userId, yearMonth.Value), out var item) ? Clone(item) : null;
        }
    }

    public Task<MonthlyAggregateDocument?> GetMonthAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken) =>
        Task.FromResult(Stored(userId, yearMonth));

    public Task<IReadOnlyList<MonthlyAggregateDocument>> ListYearAsync(string userId, int year, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<MonthlyAggregateDocument> results = _items.Values
                .Where(x => x.UserId == userId && x.Year == year)
                .OrderBy(x => x.MonthIdx)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(results);
        }
    }

    public Task<MonthlyAggregateDocument?> GetLatestBeforeAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var result = _items.Values
                .Where(x => x.UserId == userId && string.CompareOrdinal(x.YearMonth, yearMonth.Value) < 0)
                .OrderBy(x => x.Year)
                .ThenBy(x => x.MonthIdx)
                .LastOrDefault();
            return Task.FromResult(result is null ? null : Clone(result));
        }
    }

    public Task UpsertComputedAsync(MonthlyAggregateDocument aggregate, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            UpsertCount++;
            _items[(aggregate.UserId, aggregate.YearMonth)] = Clone(aggregate);
            return Task.CompletedTask;
        }
    }

    public Task<YearMonth?> GetLastAggregateMonthAsync(string userId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var last = _items.Values
                .Where(x => x.UserId == userId)
                .Select(x => YearMonth.TryParse(x.YearMonth, out var ym) ? ym : (YearMonth?)null)
                .Where(x => x is not null)
                .OrderBy(x => x!.Value.Year)
                .ThenBy(x => x!.Value.Month)
                .LastOrDefault();
            return Task.FromResult(last);
        }
    }

    private static MonthlyAggregateDocument Clone(MonthlyAggregateDocument document) =>
        new()
        {
            Id = document.Id,
            DocType = document.DocType,
            SchemaVersion = document.SchemaVersion,
            UserId = document.UserId,
            Year = document.Year,
            MonthIdx = document.MonthIdx,
            YearMonth = document.YearMonth,
            Totals = new MonthlyTotalsDocument
            {
                Entradas = document.Totals.Entradas,
                Saidas = document.Totals.Saidas,
                Aportes = document.Totals.Aportes,
                Saldo = document.Totals.Saldo,
                Investido = document.Totals.Investido
            },
            SnapshotToday = document.SnapshotToday is null
                ? null
                : new SnapshotTodayDocument
                {
                    SaldoHoje = document.SnapshotToday.SaldoHoje,
                    InvestidoHoje = document.SnapshotToday.InvestidoHoje,
                    PatrimonioHoje = document.SnapshotToday.PatrimonioHoje,
                    AsOfDate = document.SnapshotToday.AsOfDate
                },
            DailyBalances = document.DailyBalances
                .Select(x => new DailyBalanceDocument
                {
                    Day = x.Day,
                    Saldo = x.Saldo
                })
                .ToList(),
            DailyMovements = document.DailyMovements
                .Select(x => new DailyMovementDocument
                {
                    Day = x.Day,
                    Id = x.Id,
                    UserId = x.UserId,
                    Category = x.Category,
                    Kind = x.Kind,
                    Description = x.Description,
                    Amount = x.Amount,
                    CardId = x.CardId,
                    FixedRuleId = x.FixedRuleId,
                    Projected = x.Projected,
                    PurchaseDate = x.PurchaseDate
                })
                .ToList(),
            Projection = new ProjectionDocument
            {
                IncludesProjected = document.Projection.IncludesProjected,
                ProjectedCount = document.Projection.ProjectedCount,
                AsOfDate = document.Projection.AsOfDate
            },
            ByCategory = new Dictionary<string, decimal>(document.ByCategory),
            ByCard = new Dictionary<string, decimal>(document.ByCard),
            ByOwner = document.ByOwner.ToDictionary(
                x => x.Key,
                x => new OwnerTotalsDocument
                {
                    Entradas = x.Value.Entradas,
                    Saidas = x.Value.Saidas,
                    Aportes = x.Value.Aportes
                }),
            TransactionsCount = document.TransactionsCount,
            ComputedAt = document.ComputedAt,
            SourceVersion = document.SourceVersion,
            SourceWatermark = new SourceWatermarkDocument
            {
                MaxTransactionUpdatedAt = document.SourceWatermark.MaxTransactionUpdatedAt,
                ActiveTransactionsCount = document.SourceWatermark.ActiveTransactionsCount
            },
            ETag = document.ETag
        };
}

public sealed class InMemoryTransactionsProjectionRepository : ITransactionsProjectionRepository
{
    private readonly object _gate = new();
    private readonly List<TransactionProjection> _items = [];

    public void Seed(TransactionProjection transaction)
    {
        lock (_gate)
        {
            _items.Add(Clone(transaction));
        }
    }

    public Task<IReadOnlyList<TransactionProjection>> ListActiveMonthAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<TransactionProjection> results = _items
                .Where(x => x.UserId == userId && x.YearMonth == yearMonth.Value && x.DeletedAt is null)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(results);
        }
    }

    public Task<IReadOnlyList<TransactionProjection>> ListActiveRangeAsync(string userId, DateOnly fromDate, DateOnly throughDate, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<TransactionProjection> results = _items
                .Where(x => x.UserId == userId && x.Date >= fromDate && x.Date <= throughDate && x.DeletedAt is null)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(results);
        }
    }

    public Task<SourceWatermarkDocument> GetMonthWatermarkAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var items = _items
                .Where(x => x.UserId == userId && x.YearMonth == yearMonth.Value)
                .ToArray();
            return Task.FromResult(new SourceWatermarkDocument
            {
                MaxTransactionUpdatedAt = items
                    .Select(x => x.UpdatedAt)
                    .Where(x => x is not null)
                    .DefaultIfEmpty()
                    .Max(),
                ActiveTransactionsCount = items.Count(x => x.DeletedAt is null)
            });
        }
    }

    public Task<IReadOnlyDictionary<YearMonth, SourceWatermarkDocument>> GetYearWatermarksAsync(
        string userId,
        int year,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyDictionary<YearMonth, SourceWatermarkDocument> results = _items
                .Where(x => x.UserId == userId && x.YearMonth.StartsWith($"{year}-", StringComparison.Ordinal))
                .GroupBy(x => x.YearMonth)
                .Where(group => YearMonth.TryParse(group.Key, out _))
                .ToDictionary(
                    group => YearMonth.Parse(group.Key),
                    group => new SourceWatermarkDocument
                    {
                        MaxTransactionUpdatedAt = group
                            .Select(x => x.UpdatedAt)
                            .Where(x => x is not null)
                            .DefaultIfEmpty()
                            .Max(),
                        ActiveTransactionsCount = group.Count(x => x.DeletedAt is null)
                    });
            return Task.FromResult(results);
        }
    }

    public Task<MovementTotals> SumTotalsThroughAsync(string userId, DateOnly throughDate, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var totals = _items
                .Where(x => x.UserId == userId && x.Date <= throughDate && x.DeletedAt is null)
                .Aggregate(
                    new MovementTotals(0, 0, 0),
                    (current, item) => item.Kind switch
                    {
                        "in" => current with { Entradas = current.Entradas + item.Amount },
                        "out" => current with { Saidas = current.Saidas + item.Amount },
                        "invest" => current with { Aportes = current.Aportes + item.Amount },
                        _ => current
                    });
            return Task.FromResult(totals);
        }
    }

    public Task<decimal> SumInvestmentsThroughAsync(string userId, DateOnly throughDate, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_items
                .Where(x => x.UserId == userId && x.Kind == "invest" && x.Date <= throughDate && x.DeletedAt is null)
                .Sum(x => x.Amount));
        }
    }

    private static TransactionProjection Clone(TransactionProjection transaction) =>
        new()
        {
            Id = transaction.Id,
            DocType = transaction.DocType,
            UserId = transaction.UserId,
            YearMonth = transaction.YearMonth,
            Date = transaction.Date,
            PurchaseDate = transaction.PurchaseDate,
            Category = transaction.Category,
            Description = transaction.Description,
            Kind = transaction.Kind,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            CardId = transaction.CardId,
            FixedRuleId = transaction.FixedRuleId,
            Projected = transaction.Projected,
            UpdatedAt = transaction.UpdatedAt,
            DeletedAt = transaction.DeletedAt
        };
}

public sealed class InMemoryUsersReadRepository : IUsersReadRepository
{
    private readonly Dictionary<string, decimal> _startingBalances = [];

    public void Seed(string userId, decimal startingBalance) => _startingBalances[userId] = startingBalance;

    public Task<decimal> GetStartingBalanceAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult(_startingBalances.GetValueOrDefault(userId));
}

public sealed class InMemoryFixedRulesProjectionRepository : IFixedRulesProjectionRepository
{
    private readonly List<FixedRuleDocument> _items = [];

    public void Seed(FixedRuleDocument rule) => _items.Add(Clone(rule));

    public Task<IReadOnlyList<FixedRuleDocument>> ListActiveCandidatesAsync(
        string userId,
        DateOnly fromDate,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FixedRuleDocument> results = _items
            .Where(x => x.UserId == userId &&
                        x.Active &&
                        x.DeletedAt is null &&
                        FixedRuleProjectionService.TryParseDate(x.StartsAt, out var startsAt) &&
                        startsAt <= throughDate &&
                        (!FixedRuleProjectionService.TryParseDate(x.EndsAt, out var endsAt) || endsAt >= fromDate))
            .Select(Clone)
            .ToArray();
        return Task.FromResult(results);
    }

    private static FixedRuleDocument Clone(FixedRuleDocument rule) =>
        new()
        {
            Id = rule.Id,
            DocType = rule.DocType,
            UserId = rule.UserId,
            Category = rule.Category,
            Description = rule.Description,
            Amount = rule.Amount,
            CardId = rule.CardId,
            Schedule = new FixedRuleScheduleDocument
            {
                Type = rule.Schedule.Type,
                Day = rule.Schedule.Day,
                Ordinal = rule.Schedule.Ordinal,
                Period = rule.Schedule.Period
            },
            StartsAt = rule.StartsAt,
            EndsAt = rule.EndsAt,
            Active = rule.Active,
            DeletedAt = rule.DeletedAt
        };
}

public sealed class InMemoryCardsProjectionRepository : ICardsProjectionRepository
{
    private readonly Dictionary<(string UserId, string CardId), CardDocument> _items = [];

    public void Seed(CardDocument card) => _items[(card.UserId, card.Id)] = card;

    public Task<CardDocument?> GetActiveAsync(string userId, string cardId, CancellationToken cancellationToken) =>
        Task.FromResult(
            _items.TryGetValue((userId, cardId), out var card) && card.DeletedAt is null
                ? new CardDocument
                {
                    Id = card.Id,
                    DocType = card.DocType,
                    UserId = card.UserId,
                    ClosingDay = card.ClosingDay,
                    DueDay = card.DueDay,
                    DeletedAt = card.DeletedAt
                }
                : null);
}

public sealed class InMemoryPartnershipsReadRepository : IPartnershipsReadRepository
{
    private readonly Dictionary<string, PartnershipDocument> _items = [];

    public void Seed(PartnershipDocument partnership) => _items[partnership.UserId] = partnership;

    public Task<PartnershipDocument?> GetActivePartnerAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult(_items.TryGetValue(userId, out var partnership) && partnership.Status == "active"
            ? partnership
            : null);

    public Task<bool> IsActivePartnerAsync(string userId, string partnerUserId, CancellationToken cancellationToken) =>
        Task.FromResult(_items.TryGetValue(userId, out var partnership) &&
                        partnership.Status == "active" &&
                        partnership.PartnerUserId == partnerUserId);
}
