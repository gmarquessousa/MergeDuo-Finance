using MergeDuo.Transactions.Domain.Contracts;
using MergeDuo.Transactions.Domain.Documents;

namespace MergeDuo.Transactions.Domain.Abstractions;

public interface ITransactionsRepository
{
    Task<TransactionsPage> ListMonthAsync(
        string userId,
        string yearMonth,
        TransactionListFilters filters,
        int pageSize,
        string? continuationToken,
        SortDirection sort,
        CancellationToken cancellationToken);

    Task<TransactionDocument?> GetByIdAsync(
        string userId,
        string yearMonth,
        string transactionId,
        bool includeDeleted,
        CancellationToken cancellationToken);

    Task CreateAsync(TransactionDocument transaction, CancellationToken cancellationToken);
    Task<TransactionDocument> PatchAsync(TransactionDocument transaction, TransactionPatch patch, string ifMatchEtag, bool clientProvidedEtag, CancellationToken cancellationToken);
    Task CreateMovedCopyAsync(TransactionDocument transaction, CancellationToken cancellationToken);
    Task SoftDeleteAsync(TransactionDocument transaction, DateTimeOffset deletedAt, string ifMatchEtag, bool clientProvidedEtag, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionDocument>> ListGroupAsync(string userId, string groupId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionDocument>> ListTaggedAsync(string userId, bool includeTransactionDetails, CancellationToken cancellationToken);
    Task<CardUsageTotals> GetCardUsageAsync(string userId, string yearMonth, string cardId, CancellationToken cancellationToken);
}

public interface ICardsReadRepository
{
    Task<CardDocument?> GetActiveAsync(string userId, string cardId, CancellationToken cancellationToken);
}

public interface IFixedRulesReadRepository
{
    Task<FixedRuleDocument?> GetActiveAsync(string userId, string fixedRuleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListActiveTagsAsync(string userId, CancellationToken cancellationToken);
}

public interface IPartnershipsReadRepository
{
    Task<PartnershipDocument?> GetActivePartnerAsync(string userId, CancellationToken cancellationToken);
    Task<bool> IsActivePartnerAsync(string userId, string partnerUserId, CancellationToken cancellationToken);
}

public interface ITransactionIdGenerator
{
    string NewTransactionId();
    string NewGroupId();
    string FromIdempotencyKey(string prefix, string userId, string idempotencyKey, string payloadHash, int index);
}

public interface ITransactionsReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public interface ICosmosDiagnosticsRecorder
{
    void RecordCosmosOperation(string container, string operation, double requestCharge, bool throttled);
}

public sealed record TransactionsPage(IReadOnlyList<TransactionDocument> Items, string? ContinuationToken);

public sealed record TransactionListFilters(string? Category, string? CardId);

public enum SortDirection
{
    DateAsc,
    DateDesc
}

public enum OwnerFilter
{
    Me,
    Partner,
    Both
}

public sealed record CardUsageTotals(string CardId, string YearMonth, string Currency, decimal TotalAmount, int TransactionCount, int InstallmentCount);

public sealed record TransactionPatch(
    string Category,
    string Kind,
    DateOnly Date,
    DateOnly? PurchaseDate,
    string Description,
    decimal Amount,
    string Currency,
    string? OwnerLabel,
    string? CardId,
    string? FixedRuleId,
    string[] Tags,
    string? Notes,
    DateTimeOffset UpdatedAt);
