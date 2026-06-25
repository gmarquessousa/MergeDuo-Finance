using MergeDuo.Profile.Domain.Documents;

namespace MergeDuo.Profile.Domain.Rules;

public static class ProfileMapping
{
    public static PublicProfileResponse ToPublicProfile(
        UserDocument user,
        bool includeStats,
        RelationshipResponse? relationship,
        DateTimeOffset now,
        TimeSpan staleAfter)
    {
        return new PublicProfileResponse(
            user.Id,
            user.Name,
            user.Handle,
            user.AvatarInitials,
            user.AvatarUrl,
            user.MemberSince,
            includeStats ? user.Stats.ToPublicStats(now, staleAfter) : null,
            relationship);
    }

    public static PublicStatsResponse ToPublicStats(
        this UserStats? stats,
        DateTimeOffset now,
        TimeSpan staleAfter)
    {
        var value = stats ?? new UserStats();
        return new PublicStatsResponse(
            value.TransactionsTracked,
            value.ActiveMonths,
            value.LastRecomputedAt,
            StatsRules.IsStale(value, now, staleAfter));
    }

    public static UserStatsResponse ToUserStatsResponse(
        this UserStats stats,
        string source,
        DateTimeOffset now,
        TimeSpan staleAfter)
    {
        return new UserStatsResponse(
            stats.TransactionsTracked,
            stats.ActiveMonths,
            stats.LastRecomputedAt,
            source,
            StatsRules.IsStale(stats, now, staleAfter));
    }

    public static RelationshipResponse? ToActiveRelationship(this PartnershipDocument? partnership) =>
        partnership is { Status: "active" }
            ? new RelationshipResponse("active", partnership.MergedSince)
            : null;
}
