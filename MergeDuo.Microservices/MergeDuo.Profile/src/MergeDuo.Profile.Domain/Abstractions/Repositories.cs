using MergeDuo.Profile.Domain.Documents;
using MergeDuo.Profile.Domain;

namespace MergeDuo.Profile.Domain.Abstractions;

public interface IUsersRepository
{
    Task<UserDocument?> GetByIdAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserDocument>> FindByHandleAsync(string handle, CancellationToken cancellationToken);
    Task PatchStatsAsync(
        string userId,
        UserStats stats,
        DateTimeOffset updatedAt,
        string ifMatchEtag,
        CancellationToken cancellationToken);
}

public interface IPartnershipsRepository
{
    Task<PartnershipDocument?> GetRelationshipAsync(
        string currentUserId,
        string targetUserId,
        CancellationToken cancellationToken);
}

public interface ITransactionsStatsRepository
{
    Task<int> CountTrackedAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListActiveMonthsAsync(string userId, CancellationToken cancellationToken);
}

public interface IProfileQueryService
{
    Task<PublicProfileResponse?> GetProfileByIdAsync(
        UserDocument requester,
        string targetUserId,
        CancellationToken cancellationToken);

    Task<PublicProfileResponse?> GetProfileByHandleAsync(
        UserDocument requester,
        string handle,
        CancellationToken cancellationToken);
}

public interface IProfileStatsService
{
    Task<UserStatsResponse> GetCurrentStatsAsync(
        UserDocument currentUser,
        bool fresh,
        CancellationToken cancellationToken);
}

public interface IProfileReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public interface ICosmosDiagnosticsRecorder
{
    void RecordCosmosOperation(string container, string operation, double requestCharge, bool throttled);
}
