using MergeDuo.Profile.Domain.Documents;

namespace MergeDuo.Profile.Domain.Rules;

public static class StatsRules
{
    public static bool IsStale(UserStats? stats, DateTimeOffset now, TimeSpan staleAfter) =>
        stats?.LastRecomputedAt is null || now - stats.LastRecomputedAt.Value > staleAfter;

    public static bool CanSeeStats(UserDocument requester, UserDocument target, PartnershipDocument? relationship) =>
        requester.Id == target.Id || relationship is { Status: "active" };

    public static UserStats Compose(
        int transactionsTracked,
        IEnumerable<string> activeMonths,
        DateTimeOffset recomputedAt)
    {
        return new UserStats
        {
            TransactionsTracked = transactionsTracked,
            ActiveMonths = activeMonths.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Count(),
            LastRecomputedAt = recomputedAt
        };
    }
}
