using System.Collections.Concurrent;
using MergeDuo.Profile.Domain.Abstractions;
using MergeDuo.Profile.Domain.Documents;
using MergeDuo.Profile.Domain.Exceptions;

namespace MergeDuo.Profile.Tests.Fakes;

public sealed class InMemoryUsersRepository : IUsersRepository
{
    private readonly ConcurrentDictionary<string, UserDocument> _users = new();

    public IReadOnlyCollection<UserDocument> Users => _users.Values.ToArray();
    public int PatchAttempts { get; private set; }
    public int StatsConflictsBeforeSuccess { get; set; }

    public Task<UserDocument?> GetByIdAsync(string userId, CancellationToken cancellationToken)
    {
        _users.TryGetValue(userId, out var user);
        return Task.FromResult(user);
    }

    public Task<IReadOnlyList<UserDocument>> FindByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        IReadOnlyList<UserDocument> users = _users.Values
            .Where(x => string.Equals(x.Handle, handle, StringComparison.Ordinal))
            .ToArray();
        return Task.FromResult(users);
    }

    public Task PatchStatsAsync(
        string userId,
        UserStats stats,
        DateTimeOffset updatedAt,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        PatchAttempts++;
        if (StatsConflictsBeforeSuccess > 0)
        {
            StatsConflictsBeforeSuccess--;
            throw new ProfileConflictException("stats_conflict", "conflict");
        }

        if (!_users.TryGetValue(userId, out var user))
        {
            throw new ProfileNotFoundException("profile_not_found", "missing");
        }

        if (!string.Equals(user.ETag, ifMatchEtag, StringComparison.Ordinal))
        {
            throw new ProfileConflictException("stats_conflict", "etag mismatch");
        }

        user.Stats = stats;
        user.UpdatedAt = updatedAt;
        user.ETag = Guid.NewGuid().ToString("N");
        return Task.CompletedTask;
    }

    public void Add(UserDocument user)
    {
        user.ETag ??= Guid.NewGuid().ToString("N");
        _users[user.Id] = user;
    }
}

public sealed class InMemoryPartnershipsRepository : IPartnershipsRepository
{
    private readonly ConcurrentDictionary<(string UserId, string PartnerUserId), PartnershipDocument> _relationships = new();

    public Task<PartnershipDocument?> GetRelationshipAsync(
        string currentUserId,
        string targetUserId,
        CancellationToken cancellationToken)
    {
        _relationships.TryGetValue((currentUserId, targetUserId), out var relationship);
        return Task.FromResult(relationship);
    }

    public void Add(PartnershipDocument relationship) =>
        _relationships[(relationship.UserId, relationship.PartnerUserId)] = relationship;
}

public sealed class InMemoryTransactionsStatsRepository : ITransactionsStatsRepository
{
    public int TrackedCount { get; set; }
    public IReadOnlyList<string> ActiveMonths { get; set; } = [];

    public Task<int> CountTrackedAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult(TrackedCount);

    public Task<IReadOnlyList<string>> ListActiveMonthsAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult(ActiveMonths);
}

public sealed class FakeReadinessProbe : IProfileReadinessProbe
{
    public bool Ready { get; set; } = true;

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(Ready);
}
