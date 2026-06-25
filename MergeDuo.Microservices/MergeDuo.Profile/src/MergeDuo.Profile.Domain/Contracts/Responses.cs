namespace MergeDuo.Profile.Domain;

public sealed record PublicProfileResponse(
    string Id,
    string Name,
    string Handle,
    string AvatarInitials,
    string? AvatarUrl,
    string MemberSince,
    PublicStatsResponse? Stats,
    RelationshipResponse? Relationship);

public sealed record PublicStatsResponse(
    int TransactionsTracked,
    int ActiveMonths,
    DateTimeOffset? LastRecomputedAt,
    bool IsStale);

public sealed record UserStatsResponse(
    int TransactionsTracked,
    int ActiveMonths,
    DateTimeOffset? LastRecomputedAt,
    string Source,
    bool IsStale);

public sealed record RelationshipResponse(string Status, string MergedSince);
