using MergeDuo.Partnership.Domain.Documents;
using MergeDuo.Partnership.Domain.Exceptions;

namespace MergeDuo.Partnership.Domain.Rules;

public static class InviteRules
{
    private static readonly HashSet<string> AllowedChannels = new(StringComparer.Ordinal)
    {
        "link",
        "qr",
        "share"
    };

    public static string NormalizeChannel(string? channel)
    {
        var normalized = (channel ?? "").Trim().ToLowerInvariant();
        if (!AllowedChannels.Contains(normalized))
        {
            throw new PartnershipBadRequestException("invalid_channel", "Invalid invite channel.");
        }

        return normalized;
    }

    public static bool IsExpired(MergeInviteDocument invite, DateTimeOffset now) =>
        invite.Status == InviteStatuses.Pending && invite.ExpiresAt <= now;

    public static void EnsureCanAccept(MergeInviteDocument invite, string accepterUserId, DateTimeOffset now)
    {
        if (invite.InviterUserId == accepterUserId)
        {
            throw new PartnershipConflictException("self_invite_not_allowed", "Self invite is not allowed.");
        }

        if (IsExpired(invite, now) || invite.Status == InviteStatuses.Expired)
        {
            throw new PartnershipGoneException("invite_expired", "Invite expired.");
        }

        if (invite.Status == InviteStatuses.Revoked)
        {
            throw new PartnershipGoneException("invite_revoked", "Invite was revoked.");
        }

        if (invite.Status == InviteStatuses.Accepted
            && !string.Equals(invite.AcceptedBy?.UserId, accepterUserId, StringComparison.Ordinal))
        {
            throw new PartnershipConflictException("invite_already_accepted", "Invite was already accepted.");
        }
    }

    public static void EnsureCanPreview(MergeInviteDocument invite, DateTimeOffset now)
    {
        if (IsExpired(invite, now) || invite.Status == InviteStatuses.Expired)
        {
            throw new PartnershipGoneException("invite_expired", "Invite expired.");
        }

        if (invite.Status == InviteStatuses.Revoked)
        {
            throw new PartnershipGoneException("invite_revoked", "Invite was revoked.");
        }

        if (invite.Status == InviteStatuses.Accepted)
        {
            throw new PartnershipConflictException("invite_already_accepted", "Invite was already accepted.");
        }
    }
}
