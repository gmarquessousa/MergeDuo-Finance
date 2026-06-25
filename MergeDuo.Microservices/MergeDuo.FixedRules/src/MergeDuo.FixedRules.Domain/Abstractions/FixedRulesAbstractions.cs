using MergeDuo.FixedRules.Domain.Documents;

namespace MergeDuo.FixedRules.Domain.Abstractions;

public interface IFixedRulesRepository
{
    Task<IReadOnlyList<FixedRuleDocument>> ListAsync(
        string userId,
        FixedRuleListFilter filter,
        CancellationToken cancellationToken);

    Task<FixedRuleDocument?> GetByIdAsync(
        string userId,
        string fixedRuleId,
        bool includeDeleted,
        CancellationToken cancellationToken);

    Task CreateAsync(FixedRuleDocument rule, CancellationToken cancellationToken);

    Task<FixedRuleDocument> PatchAsync(
        FixedRuleDocument rule,
        FixedRulePatch patch,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken);

    Task SoftDeleteAsync(
        FixedRuleDocument rule,
        DateTimeOffset deletedAt,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken);
}

public interface ICardsReadRepository
{
    Task<CardProjection?> GetActiveCardAsync(string userId, string cardId, CancellationToken cancellationToken);
}

public interface IFixedRuleIdGenerator
{
    string NewId();
}

public interface IFixedRulesReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public interface ICosmosDiagnosticsRecorder
{
    void RecordCosmosOperation(string container, string operation, double requestCharge, bool throttled);
}

public interface IBusinessCalendar
{
    bool IsBusinessDay(DateOnly date);
    DateOnly NthBusinessDay(int year, int month, int ordinal);
}

public sealed record FixedRuleListFilter(string? Category, FixedRuleActiveFilter Active);

public enum FixedRuleActiveFilter
{
    Active,
    Inactive,
    All
}

public sealed record FixedRulePatch
{
    public string? Category { get; init; }
    public bool HasCategory { get; init; }
    public string? Description { get; init; }
    public bool HasDescription { get; init; }
    public decimal? Amount { get; init; }
    public bool HasAmount { get; init; }
    public string? CardId { get; init; }
    public bool HasCardId { get; init; }
    public string[]? Tags { get; init; }
    public bool HasTags { get; init; }
    public FixedRuleScheduleDocument? Schedule { get; init; }
    public bool HasSchedule { get; init; }
    public string? StartsAt { get; init; }
    public bool HasStartsAt { get; init; }
    public string? EndsAt { get; init; }
    public bool HasEndsAt { get; init; }
    public bool? Active { get; init; }
    public bool HasActive { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
