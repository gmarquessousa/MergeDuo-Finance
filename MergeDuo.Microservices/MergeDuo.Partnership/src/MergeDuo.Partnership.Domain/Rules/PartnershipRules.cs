using MergeDuo.Partnership.Domain.Documents;
using MergeDuo.Partnership.Domain.Exceptions;

namespace MergeDuo.Partnership.Domain.Rules;

public static class PartnershipRules
{
    public static string PartnershipId(string firstUserId, string secondUserId)
    {
        var ordered = OrderUserIds(firstUserId, secondUserId);
        return $"pair_{ordered.First}_{ordered.Second}";
    }

    public static string DocumentId(string firstUserId, string secondUserId, string ownerUserId) =>
        $"{PartnershipId(firstUserId, secondUserId)}_{ownerUserId}";

    public static bool BlocksNewPartnership(PartnershipDocument? partnership) =>
        partnership is not null
        && (partnership.Status == PartnershipStatuses.Active || partnership.Status == PartnershipStatuses.Paused);

    public static PartnerSnapshot Snapshot(UserSummaryDocument user) =>
        new()
        {
            UserId = user.Id,
            Name = user.Name,
            Handle = user.Handle,
            Initials = !string.IsNullOrWhiteSpace(user.Initials)
                ? user.Initials
                : !string.IsNullOrWhiteSpace(user.AvatarInitials)
                    ? user.AvatarInitials
                    : BuildInitials(user.Name)
        };

    public static AcceptedBySnapshot AcceptedBy(UserSummaryDocument user, DateTimeOffset acceptedAt) =>
        new()
        {
            UserId = user.Id,
            Name = user.Name,
            Handle = user.Handle,
            AcceptedAt = acceptedAt
        };

    public static (PartnershipDocument InviterDocument, PartnershipDocument AccepterDocument) BuildPair(
        UserSummaryDocument inviter,
        UserSummaryDocument accepter,
        DateTimeOffset now)
    {
        var partnershipId = PartnershipId(inviter.Id, accepter.Id);
        var inviterDocument = BuildDocument(inviter, accepter, partnershipId, now);
        var accepterDocument = BuildDocument(accepter, inviter, partnershipId, now);
        return (inviterDocument, accepterDocument);
    }

    public static void EnsureCanPause(PartnershipDocument partnership)
    {
        if (partnership.Status == PartnershipStatuses.Ended)
        {
            throw new PartnershipConflictException("partnership_already_ended", "Partnership already ended.");
        }
    }

    public static void EnsureCanEnd(PartnershipDocument partnership)
    {
        if (partnership.Status is not PartnershipStatuses.Active
            and not PartnershipStatuses.Paused
            and not PartnershipStatuses.Ended)
        {
            throw new PartnershipConflictException("partnership_already_ended", "Partnership cannot be ended.");
        }
    }

    private static PartnershipDocument BuildDocument(
        UserSummaryDocument owner,
        UserSummaryDocument partner,
        string partnershipId,
        DateTimeOffset now) =>
        new()
        {
            Id = DocumentId(owner.Id, partner.Id, owner.Id),
            PartnershipId = partnershipId,
            UserId = owner.Id,
            PartnerUserId = partner.Id,
            Status = PartnershipStatuses.Active,
            PartnerSnapshot = Snapshot(partner),
            StartingBalance = owner.Financial?.StartingBalance ?? 0m,
            MergedSince = DateOnly.FromDateTime(now.UtcDateTime),
            CreatedAt = now,
            UpdatedAt = now
        };

    private static (string First, string Second) OrderUserIds(string firstUserId, string secondUserId) =>
        string.CompareOrdinal(firstUserId, secondUserId) <= 0
            ? (firstUserId, secondUserId)
            : (secondUserId, firstUserId);

    private static string BuildInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return "";
        }

        var initials = parts.Take(2).Select(x => char.ToUpperInvariant(x[0])).ToArray();
        return new string(initials);
    }
}
