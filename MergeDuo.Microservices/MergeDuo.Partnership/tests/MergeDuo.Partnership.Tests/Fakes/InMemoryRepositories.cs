using System.Collections.Concurrent;
using MergeDuo.Partnership.Domain.Abstractions;
using MergeDuo.Partnership.Domain.Documents;
using MergeDuo.Partnership.Domain.Exceptions;

namespace MergeDuo.Partnership.Tests.Fakes;

public sealed class InMemoryUsersReadRepository : IUsersReadRepository
{
    private readonly ConcurrentDictionary<string, UserSummaryDocument> _users = new();

    public Task<UserSummaryDocument?> GetByIdAsync(string userId, CancellationToken cancellationToken)
    {
        _users.TryGetValue(userId, out var user);
        return Task.FromResult(user);
    }

    public Task<UserSummaryDocument?> GetActiveUserSummaryAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        _users.TryGetValue(userId, out var user);
        return Task.FromResult(user?.DeletedAt is null ? user : null);
    }

    public Task<UserSummaryDocument?> GetUserForPartnershipAsync(
        string userId,
        CancellationToken cancellationToken) =>
        GetActiveUserSummaryAsync(userId, cancellationToken);

    public void Add(UserSummaryDocument user)
    {
        user.ETag ??= Guid.NewGuid().ToString("N");
        _users[user.Id] = user;
    }
}

public sealed class InMemoryInvitesRepository : IInvitesRepository
{
    private readonly ConcurrentDictionary<string, MergeInviteDocument> _invites = new();

    public IReadOnlyCollection<MergeInviteDocument> All => _invites.Values.ToArray();

    public Task<MergeInviteDocument?> GetPendingForInviterAsync(
        string inviterUserId,
        CancellationToken cancellationToken)
    {
        var invite = _invites.Values
            .Where(x => x.InviterUserId == inviterUserId && x.Status == InviteStatuses.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        return Task.FromResult(invite);
    }

    public Task<IReadOnlyList<MergeInviteDocument>> FindByTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MergeInviteDocument> matches = _invites.Values
            .Where(x => x.Token == token)
            .OrderBy(x => x.CreatedAt)
            .ToArray();
        return Task.FromResult(matches);
    }

    public Task CreateAsync(MergeInviteDocument invite, CancellationToken cancellationToken)
    {
        invite.ETag ??= Guid.NewGuid().ToString("N");
        _invites[invite.Id] = invite;
        return Task.CompletedTask;
    }

    public Task MarkAcceptedAsync(
        MergeInviteDocument invite,
        AcceptedBySnapshot acceptedBy,
        string partnershipId,
        DateTimeOffset acceptedAt,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        var stored = Require(invite.Id);
        EnsureEtag(stored, ifMatchEtag, "invite_already_accepted");
        stored.Status = InviteStatuses.Accepted;
        stored.AcceptedBy = acceptedBy;
        stored.PartnershipId = partnershipId;
        stored.AcceptedAt = acceptedAt;
        stored.UpdatedAt = acceptedAt;
        stored.Ttl = -1;
        stored.ETag = Guid.NewGuid().ToString("N");
        return Task.CompletedTask;
    }

    public Task MarkRevokedAsync(
        MergeInviteDocument invite,
        DateTimeOffset revokedAt,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        var stored = Require(invite.Id);
        EnsureEtag(stored, ifMatchEtag, "invite_already_accepted");
        stored.Status = InviteStatuses.Revoked;
        stored.RevokedAt = revokedAt;
        stored.UpdatedAt = revokedAt;
        stored.ETag = Guid.NewGuid().ToString("N");
        return Task.CompletedTask;
    }

    public Task MarkExpiredAsync(
        MergeInviteDocument invite,
        DateTimeOffset expiredAt,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        var stored = Require(invite.Id);
        EnsureEtag(stored, ifMatchEtag, "invite_already_accepted");
        stored.Status = InviteStatuses.Expired;
        stored.UpdatedAt = expiredAt;
        stored.ETag = Guid.NewGuid().ToString("N");
        return Task.CompletedTask;
    }

    public void Add(MergeInviteDocument invite)
    {
        invite.ETag ??= Guid.NewGuid().ToString("N");
        _invites[invite.Id] = invite;
    }

    private MergeInviteDocument Require(string id) =>
        _invites.TryGetValue(id, out var invite)
            ? invite
            : throw new PartnershipNotFoundException("invite_not_found", "missing");

    private static void EnsureEtag(MergeInviteDocument invite, string etag, string code)
    {
        if (!string.Equals(invite.ETag, etag, StringComparison.Ordinal))
        {
            throw new PartnershipConflictException(code, "conflict");
        }
    }
}

public sealed class InMemoryPartnershipsRepository : IPartnershipsRepository
{
    private readonly ConcurrentDictionary<(string UserId, string Id), PartnershipDocument> _partnerships = new();

    public IReadOnlyCollection<PartnershipDocument> All => _partnerships.Values.ToArray();

    public Task<PartnershipDocument?> GetCurrentAsync(string userId, CancellationToken cancellationToken)
    {
        var document = _partnerships.Values
            .Where(x => x.UserId == userId && x.Status is PartnershipStatuses.Active or PartnershipStatuses.Paused)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefault();
        return Task.FromResult(document);
    }

    public Task<PartnershipDocument?> GetByIdAsync(
        string userId,
        string id,
        CancellationToken cancellationToken)
    {
        _partnerships.TryGetValue((userId, id), out var document);
        return Task.FromResult(document);
    }

    public Task CreateIfAbsentAsync(PartnershipDocument document, CancellationToken cancellationToken)
    {
        document.ETag ??= Guid.NewGuid().ToString("N");
        _partnerships.TryAdd((document.UserId, document.Id), document);
        return Task.CompletedTask;
    }

    public async Task EnsurePairAsync(
        PartnershipDocument ownerDocument,
        PartnershipDocument mirrorDocument,
        CancellationToken cancellationToken)
    {
        await CreateIfAbsentAsync(ownerDocument, cancellationToken);
        await CreateIfAbsentAsync(mirrorDocument, cancellationToken);
    }

    public Task PatchStatusAsync(
        string userId,
        string id,
        string status,
        DateTimeOffset updatedAt,
        DateTimeOffset? endedAt,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        if (!_partnerships.TryGetValue((userId, id), out var document))
        {
            throw new PartnershipNotFoundException("partnership_not_found", "missing");
        }

        if (!string.Equals(document.ETag, ifMatchEtag, StringComparison.Ordinal))
        {
            throw new PartnershipConflictException("partnership_already_exists", "conflict");
        }

        document.Status = status;
        document.UpdatedAt = updatedAt;
        document.EndedAt = endedAt ?? document.EndedAt;
        document.ETag = Guid.NewGuid().ToString("N");
        return Task.CompletedTask;
    }

    public void Add(PartnershipDocument document)
    {
        document.ETag ??= Guid.NewGuid().ToString("N");
        _partnerships[(document.UserId, document.Id)] = document;
    }
}

public sealed class FakeReadinessProbe : IPartnershipReadinessProbe
{
    public bool Ready { get; set; } = true;

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(Ready);
}
