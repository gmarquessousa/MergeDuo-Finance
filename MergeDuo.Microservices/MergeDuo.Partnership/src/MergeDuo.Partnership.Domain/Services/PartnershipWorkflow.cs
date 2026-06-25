using MergeDuo.Partnership.Domain.Abstractions;
using MergeDuo.Partnership.Domain.Contracts;
using MergeDuo.Partnership.Domain.Documents;
using MergeDuo.Partnership.Domain.Exceptions;
using MergeDuo.Partnership.Domain.Options;
using MergeDuo.Partnership.Domain.Rules;

namespace MergeDuo.Partnership.Domain.Services;

public interface IPartnershipWorkflow
{
    Task<CreateInviteResponse> CreateInviteAsync(
        UserSummaryDocument currentUser,
        CreateInviteRequest request,
        CancellationToken cancellationToken);

    Task<InvitePreviewResponse> PreviewInviteAsync(string token, CancellationToken cancellationToken);

    Task<PartnershipStatusResponse> RevokeInviteAsync(
        string currentUserId,
        string token,
        CancellationToken cancellationToken);

    Task<AcceptInviteResponse> AcceptInviteAsync(
        UserSummaryDocument currentUser,
        string token,
        CancellationToken cancellationToken);

    Task<CurrentPartnershipResponse> GetCurrentAsync(string currentUserId, CancellationToken cancellationToken);

    Task<PartnershipStatusResponse> PauseAsync(
        string currentUserId,
        string partnershipDocumentId,
        CancellationToken cancellationToken);

    Task<PartnershipStatusResponse> EndAsync(
        string currentUserId,
        string partnershipDocumentId,
        CancellationToken cancellationToken);
}

public sealed class PartnershipWorkflow(
    IUsersReadRepository users,
    IInvitesRepository invites,
    IPartnershipsRepository partnerships,
    TimeProvider clock,
    InviteOptions inviteOptions,
    PublicAppOptions publicAppOptions) : IPartnershipWorkflow
{
    public async Task<CreateInviteResponse> CreateInviteAsync(
        UserSummaryDocument currentUser,
        CreateInviteRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.Id))
        {
            throw new PartnershipBadRequestException("invalid_user", "Current user is invalid.");
        }

        var channel = InviteRules.NormalizeChannel(request.Channel);
        await EnsureUserCanStartPartnershipAsync(currentUser.Id, cancellationToken);

        var now = clock.GetUtcNow();
        var existing = await invites.GetPendingForInviterAsync(currentUser.Id, cancellationToken);
        if (existing is not null && !InviteRules.IsExpired(existing, now))
        {
            return ToCreateInviteResponse(existing);
        }

        if (existing is not null && existing.ETag is not null)
        {
            await invites.MarkExpiredAsync(existing, now, existing.ETag, cancellationToken);
        }

        var token = InviteTokenRules.Generate(inviteOptions.TokenEntropyBytes);
        var expiresAt = now.AddHours(inviteOptions.ExpiresAfterHours);
        var invite = new MergeInviteDocument
        {
            Id = $"invite_{Guid.NewGuid():N}",
            Token = token,
            InviterUserId = currentUser.Id,
            Channel = channel,
            Status = InviteStatuses.Pending,
            InviterSnapshot = PartnershipRules.Snapshot(currentUser),
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = expiresAt
        };

        await invites.CreateAsync(invite, cancellationToken);
        return ToCreateInviteResponse(invite);
    }

    public async Task<InvitePreviewResponse> PreviewInviteAsync(string token, CancellationToken cancellationToken)
    {
        var invite = await GetSingleInviteByTokenAsync(token, cancellationToken);
        InviteRules.EnsureCanPreview(invite, clock.GetUtcNow());
        return new InvitePreviewResponse(
            invite.Token,
            invite.Status,
            ToResponse(invite.InviterSnapshot),
            invite.ExpiresAt);
    }

    public async Task<PartnershipStatusResponse> RevokeInviteAsync(
        string currentUserId,
        string token,
        CancellationToken cancellationToken)
    {
        var invite = await GetSingleInviteByTokenAsync(token, cancellationToken);
        if (!string.Equals(invite.InviterUserId, currentUserId, StringComparison.Ordinal))
        {
            throw new PartnershipAccessException("not_invite_owner", "Only the invite owner can revoke it.");
        }

        var now = clock.GetUtcNow();
        if (InviteRules.IsExpired(invite, now))
        {
            throw new PartnershipGoneException("invite_expired", "Invite expired.");
        }

        if (invite.Status == InviteStatuses.Accepted)
        {
            throw new PartnershipConflictException("invite_already_accepted", "Accepted invites cannot be revoked.");
        }

        if (invite.Status == InviteStatuses.Pending)
        {
            await invites.MarkRevokedAsync(invite, now, RequireEtag(invite.ETag), cancellationToken);
            invite.Status = InviteStatuses.Revoked;
            invite.UpdatedAt = now;
            invite.RevokedAt = now;
        }

        return new PartnershipStatusResponse(
            invite.Id,
            invite.PartnershipId ?? "",
            invite.Status,
            invite.UpdatedAt,
            invite.RevokedAt);
    }

    public async Task<AcceptInviteResponse> AcceptInviteAsync(
        UserSummaryDocument currentUser,
        string token,
        CancellationToken cancellationToken)
    {
        var invite = await GetSingleInviteByTokenAsync(token, cancellationToken);
        var now = clock.GetUtcNow();
        InviteRules.EnsureCanAccept(invite, currentUser.Id, now);

        if (invite.Status == InviteStatuses.Accepted
            && string.Equals(invite.AcceptedBy?.UserId, currentUser.Id, StringComparison.Ordinal)
            && invite.PartnershipId is not null)
        {
            return AcceptedResponse(invite.PartnershipId, invite.InviterUserId, currentUser.Id, currentUser.Id);
        }

        await EnsureUserCanStartPartnershipAsync(currentUser.Id, cancellationToken);

        var inviter = await users.GetUserForPartnershipAsync(invite.InviterUserId, cancellationToken);
        if (inviter is null)
        {
            throw new PartnershipDependencyException("partnership_dependency_unavailable", "Partnership dependency unavailable.");
        }

        var (inviterDocument, accepterDocument) = PartnershipRules.BuildPair(inviter, currentUser, now);
        var acceptedBy = PartnershipRules.AcceptedBy(currentUser, now);

        await invites.MarkAcceptedAsync(
            invite,
            acceptedBy,
            inviterDocument.PartnershipId,
            now,
            RequireEtag(invite.ETag),
            cancellationToken);

        await partnerships.EnsurePairAsync(inviterDocument, accepterDocument, cancellationToken);
        return AcceptedResponse(inviterDocument.PartnershipId, invite.InviterUserId, currentUser.Id, currentUser.Id);
    }

    public async Task<CurrentPartnershipResponse> GetCurrentAsync(
        string currentUserId,
        CancellationToken cancellationToken)
    {
        var partnership = await partnerships.GetCurrentAsync(currentUserId, cancellationToken);
        return new CurrentPartnershipResponse(partnership is null ? null : ToPartnershipResponse(partnership));
    }

    public async Task<PartnershipStatusResponse> PauseAsync(
        string currentUserId,
        string partnershipDocumentId,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var document = await GetPartnershipDocumentAsync(currentUserId, partnershipDocumentId, cancellationToken);
        PartnershipRules.EnsureCanPause(document);
        var mirror = await GetRequiredMirrorAsync(document, cancellationToken);

        if (document.Status == PartnershipStatuses.Paused && mirror.Status == PartnershipStatuses.Paused)
        {
            return ToStatusResponse(document);
        }

        if (mirror.Status == PartnershipStatuses.Ended)
        {
            throw new PartnershipDependencyException("partnership_mirror_incomplete", "Partnership mirror is incomplete.");
        }

        await partnerships.PatchStatusAsync(
            document.UserId,
            document.Id,
            PartnershipStatuses.Paused,
            now,
            null,
            RequireEtag(document.ETag),
            cancellationToken);
        await partnerships.PatchStatusAsync(
            mirror.UserId,
            mirror.Id,
            PartnershipStatuses.Paused,
            now,
            null,
            RequireEtag(mirror.ETag),
            cancellationToken);

        document.Status = PartnershipStatuses.Paused;
        document.UpdatedAt = now;
        return ToStatusResponse(document);
    }

    public async Task<PartnershipStatusResponse> EndAsync(
        string currentUserId,
        string partnershipDocumentId,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var document = await GetPartnershipDocumentAsync(currentUserId, partnershipDocumentId, cancellationToken);
        PartnershipRules.EnsureCanEnd(document);
        var mirror = await GetRequiredMirrorAsync(document, cancellationToken);

        if (document.Status == PartnershipStatuses.Ended && mirror.Status == PartnershipStatuses.Ended)
        {
            return ToStatusResponse(document);
        }

        await partnerships.PatchStatusAsync(
            document.UserId,
            document.Id,
            PartnershipStatuses.Ended,
            now,
            now,
            RequireEtag(document.ETag),
            cancellationToken);
        await partnerships.PatchStatusAsync(
            mirror.UserId,
            mirror.Id,
            PartnershipStatuses.Ended,
            now,
            now,
            RequireEtag(mirror.ETag),
            cancellationToken);

        document.Status = PartnershipStatuses.Ended;
        document.UpdatedAt = now;
        document.EndedAt = now;
        return ToStatusResponse(document);
    }

    private async Task EnsureUserCanStartPartnershipAsync(string userId, CancellationToken cancellationToken)
    {
        var current = await partnerships.GetCurrentAsync(userId, cancellationToken);
        if (PartnershipRules.BlocksNewPartnership(current))
        {
            throw new PartnershipConflictException("partnership_already_exists", "User already has a current partnership.");
        }
    }

    private async Task<MergeInviteDocument> GetSingleInviteByTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        InviteTokenRules.EnsureValid(token);
        var matches = await invites.FindByTokenAsync(token, cancellationToken);
        return matches.Count switch
        {
            0 => throw new PartnershipNotFoundException("invite_not_found", "Invite not found."),
            1 => matches[0],
            _ => throw new PartnershipConflictException("duplicate_invite_token_detected", "Duplicate invite token detected.")
        };
    }

    private async Task<PartnershipDocument> GetPartnershipDocumentAsync(
        string currentUserId,
        string partnershipDocumentId,
        CancellationToken cancellationToken)
    {
        var document = await partnerships.GetByIdAsync(currentUserId, partnershipDocumentId, cancellationToken);
        return document
            ?? throw new PartnershipNotFoundException("partnership_not_found", "Partnership not found.");
    }

    private async Task<PartnershipDocument> GetRequiredMirrorAsync(
        PartnershipDocument document,
        CancellationToken cancellationToken)
    {
        var mirrorId = PartnershipRules.DocumentId(document.UserId, document.PartnerUserId, document.PartnerUserId);
        var mirror = await partnerships.GetByIdAsync(document.PartnerUserId, mirrorId, cancellationToken);
        return mirror
            ?? throw new PartnershipDependencyException("partnership_mirror_missing", "Partnership mirror is missing.");
    }

    private CreateInviteResponse ToCreateInviteResponse(MergeInviteDocument invite)
    {
        var inviteUrl = BuildInviteUrl(invite.Token);
        return new CreateInviteResponse(
            invite.Token,
            invite.Status,
            inviteUrl,
            inviteUrl,
            invite.ExpiresAt);
    }

    private string BuildInviteUrl(string token)
    {
        var baseUrl = publicAppOptions.InviteBaseUrl.Trim();
        if (baseUrl.Contains("{token}", StringComparison.Ordinal))
        {
            return baseUrl.Replace("{token}", token, StringComparison.Ordinal);
        }

        return $"{baseUrl.TrimEnd('/')}/{token}";
    }

    private static PartnershipResponse ToPartnershipResponse(PartnershipDocument document) =>
        new(
            document.Id,
            document.PartnershipId,
            document.Status,
            document.UserId,
            document.PartnerUserId,
            ToResponse(document.PartnerSnapshot),
            document.StartingBalance,
            document.MergedSince,
            document.CreatedAt,
            document.UpdatedAt,
            document.EndedAt);

    private static PartnershipStatusResponse ToStatusResponse(PartnershipDocument document) =>
        new(document.Id, document.PartnershipId, document.Status, document.UpdatedAt, document.EndedAt);

    private static AcceptInviteResponse AcceptedResponse(
        string partnershipId,
        string inviterUserId,
        string accepterUserId,
        string ownerUserId) =>
        new(
            partnershipId,
            PartnershipRules.DocumentId(inviterUserId, accepterUserId, ownerUserId),
            PartnershipStatuses.Active);

    private static PartnerSnapshotResponse ToResponse(PartnerSnapshot snapshot) =>
        new(snapshot.UserId, snapshot.Name, snapshot.Handle, snapshot.Initials);

    private static string RequireEtag(string? etag) =>
        !string.IsNullOrWhiteSpace(etag)
            ? etag
            : throw new PartnershipDependencyException("partnership_dependency_unavailable", "Missing concurrency token.");
}
