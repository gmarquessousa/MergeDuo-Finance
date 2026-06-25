using MergeDuo.Cards.Domain.Contracts;
using MergeDuo.Cards.Domain.Documents;

namespace MergeDuo.Cards.Domain.Abstractions;

public interface ICardsRepository
{
    Task<IReadOnlyList<CardDocument>> ListActiveAsync(string userId, CancellationToken cancellationToken);
    Task<CardDocument?> GetByIdAsync(string userId, string cardId, bool includeDeleted, CancellationToken cancellationToken);
    Task CreateAsync(CardDocument card, CancellationToken cancellationToken);
    Task<CardDocument> PatchAsync(
        CardDocument card,
        CardPatch patch,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken);
    Task SoftDeleteAsync(
        CardDocument card,
        DateTimeOffset deletedAt,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken);
}

public interface ICardUsageClient
{
    Task<CardUsageTotals> GetUsageAsync(
        string userId,
        string cardId,
        string yearMonth,
        string? authorizationHeader,
        CancellationToken cancellationToken);
}

public interface ICardIdGenerator
{
    string NewId();
}

public interface ICardsReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public interface ICosmosDiagnosticsRecorder
{
    void RecordCosmosOperation(string container, string operation, double requestCharge, bool throttled);
}

public sealed record CardPatch(
    string? Title,
    int? ClosingDay,
    int? DueDay,
    string? Currency,
    DateTimeOffset UpdatedAt)
{
    public bool HasChanges =>
        Title is not null || ClosingDay is not null || DueDay is not null || Currency is not null;
}
