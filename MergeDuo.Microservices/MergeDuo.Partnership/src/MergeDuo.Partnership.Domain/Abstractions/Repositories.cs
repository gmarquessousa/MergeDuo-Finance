using MergeDuo.Partnership.Domain.Documents;

namespace MergeDuo.Partnership.Domain.Abstractions;

public interface IUsersReadRepository
{
    Task<UserSummaryDocument?> GetByIdAsync(string userId, CancellationToken cancellationToken);
    Task<UserSummaryDocument?> GetActiveUserSummaryAsync(string userId, CancellationToken cancellationToken);
    Task<UserSummaryDocument?> GetUserForPartnershipAsync(string userId, CancellationToken cancellationToken);
}

public interface IInvitesRepository
{
    Task<MergeInviteDocument?> GetPendingForInviterAsync(string inviterUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MergeInviteDocument>> FindByTokenAsync(string token, CancellationToken cancellationToken);
    Task CreateAsync(MergeInviteDocument invite, CancellationToken cancellationToken);
    Task MarkAcceptedAsync(
        MergeInviteDocument invite,
        AcceptedBySnapshot acceptedBy,
        string partnershipId,
        DateTimeOffset acceptedAt,
        string ifMatchEtag,
        CancellationToken cancellationToken);
    Task MarkRevokedAsync(
        MergeInviteDocument invite,
        DateTimeOffset revokedAt,
        string ifMatchEtag,
        CancellationToken cancellationToken);
    Task MarkExpiredAsync(
        MergeInviteDocument invite,
        DateTimeOffset expiredAt,
        string ifMatchEtag,
        CancellationToken cancellationToken);
}

public interface IPartnershipsRepository
{
    Task<PartnershipDocument?> GetCurrentAsync(string userId, CancellationToken cancellationToken);
    Task<PartnershipDocument?> GetByIdAsync(string userId, string id, CancellationToken cancellationToken);
    Task CreateIfAbsentAsync(PartnershipDocument document, CancellationToken cancellationToken);
    Task EnsurePairAsync(
        PartnershipDocument ownerDocument,
        PartnershipDocument mirrorDocument,
        CancellationToken cancellationToken);
    Task PatchStatusAsync(
        string userId,
        string id,
        string status,
        DateTimeOffset updatedAt,
        DateTimeOffset? endedAt,
        string ifMatchEtag,
        CancellationToken cancellationToken);
}

public interface IPartnershipReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public interface ICosmosDiagnosticsRecorder
{
    void RecordCosmosOperation(string container, string operation, double requestCharge, bool throttled);
}
