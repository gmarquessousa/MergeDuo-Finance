using MergeDuo.Aggregates.Domain.Contracts;
using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Rules;

namespace MergeDuo.Aggregates.Domain.Abstractions;

public interface IMonthlyAggregatesRepository
{
    Task<MonthlyAggregateDocument?> GetMonthAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken);
    Task<IReadOnlyList<MonthlyAggregateDocument>> ListYearAsync(string userId, int year, CancellationToken cancellationToken);
    Task<MonthlyAggregateDocument?> GetLatestBeforeAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken);
    Task UpsertComputedAsync(MonthlyAggregateDocument aggregate, CancellationToken cancellationToken);
    Task<YearMonth?> GetLastAggregateMonthAsync(string userId, CancellationToken cancellationToken);
}

public interface ITransactionsProjectionRepository
{
    Task<IReadOnlyList<TransactionProjection>> ListActiveMonthAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionProjection>> ListActiveRangeAsync(string userId, DateOnly fromDate, DateOnly throughDate, CancellationToken cancellationToken);
    Task<SourceWatermarkDocument> GetMonthWatermarkAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<YearMonth, SourceWatermarkDocument>> GetYearWatermarksAsync(string userId, int year, CancellationToken cancellationToken);
    Task<MovementTotals> SumTotalsThroughAsync(string userId, DateOnly throughDate, CancellationToken cancellationToken);
    Task<decimal> SumInvestmentsThroughAsync(string userId, DateOnly throughDate, CancellationToken cancellationToken);
}

public sealed record MovementTotals(decimal Entradas, decimal Saidas, decimal Aportes)
{
    public decimal SaldoDelta => Entradas - Saidas - Aportes;
}

public interface IUsersReadRepository
{
    Task<decimal> GetStartingBalanceAsync(string userId, CancellationToken cancellationToken);
}

public interface IFixedRulesProjectionRepository
{
    Task<IReadOnlyList<FixedRuleDocument>> ListActiveCandidatesAsync(string userId, DateOnly fromDate, DateOnly throughDate, CancellationToken cancellationToken);
}

public interface ICardsProjectionRepository
{
    Task<CardDocument?> GetActiveAsync(string userId, string cardId, CancellationToken cancellationToken);
}

public interface IPartnershipsReadRepository
{
    Task<PartnershipDocument?> GetActivePartnerAsync(string userId, CancellationToken cancellationToken);
    Task<bool> IsActivePartnerAsync(string userId, string partnerUserId, CancellationToken cancellationToken);
}

public interface IAggregateQueryService
{
    Task<MonthlyAggregateResponse> GetMonthAsync(string requesterUserId, string targetUserId, YearMonth yearMonth, CancellationToken cancellationToken);
    Task<YearAggregatesResponse> GetYearAsync(string requesterUserId, string targetUserId, int year, CancellationToken cancellationToken);
}

public interface IAggregateRecomputeService
{
    Task RecomputeForChangeAsync(string userId, YearMonth changedMonth, CancellationToken cancellationToken);
    Task RecomputeForFixedRuleChangeAsync(FixedRuleDocument fixedRule, CancellationToken cancellationToken);
    Task RecomputeForPartnershipChangeAsync(PartnershipDocument partnership, CancellationToken cancellationToken);
    Task RecomputeMonthAsync(string userId, YearMonth yearMonth, CancellationToken cancellationToken);
    Task BackfillAggregatesAsync(string userId, YearMonth from, YearMonth to, CancellationToken cancellationToken);
    Task BackfillYearAsync(string userId, int year, CancellationToken cancellationToken);
}

public interface IAggregatesReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public interface ICosmosDiagnosticsRecorder
{
    void RecordCosmosOperation(string container, string operation, double requestCharge, bool throttled);
}
