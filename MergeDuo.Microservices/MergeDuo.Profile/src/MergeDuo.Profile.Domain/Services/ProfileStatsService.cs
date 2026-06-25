using MergeDuo.Profile.Domain.Abstractions;
using MergeDuo.Profile.Domain;
using MergeDuo.Profile.Domain.Documents;
using MergeDuo.Profile.Domain.Exceptions;
using MergeDuo.Profile.Domain.Options;
using MergeDuo.Profile.Domain.Rules;

namespace MergeDuo.Profile.Domain.Services;

public sealed class ProfileStatsService(
    IUsersRepository users,
    ITransactionsStatsRepository transactions,
    StatsOptions options,
    TimeProvider clock) : IProfileStatsService
{
    private TimeSpan StaleAfter => TimeSpan.FromMinutes(options.StaleAfterMinutes);

    public async Task<UserStatsResponse> GetCurrentStatsAsync(
        UserDocument currentUser,
        bool fresh,
        CancellationToken cancellationToken)
    {
        if (!fresh && currentUser.Stats is not null)
        {
            return currentUser.Stats.ToUserStatsResponse("cache", clock.GetUtcNow(), StaleAfter);
        }

        var stats = await RecomputeAsync(currentUser.Id, cancellationToken);
        await PatchWithRetryAsync(currentUser, stats, cancellationToken);
        return stats.ToUserStatsResponse("recomputed", clock.GetUtcNow(), StaleAfter);
    }

    private async Task<UserStats> RecomputeAsync(string userId, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.DependencyTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var transactionsTracked = await transactions.CountTrackedAsync(userId, linked.Token);
            var activeMonths = await transactions.ListActiveMonthsAsync(userId, linked.Token);

            return StatsRules.Compose(
                transactionsTracked,
                activeMonths,
                clock.GetUtcNow());
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProfileDependencyException("Profile dependency timed out.", ex);
        }
    }

    private async Task PatchWithRetryAsync(
        UserDocument currentUser,
        UserStats stats,
        CancellationToken cancellationToken)
    {
        var etag = currentUser.ETag;
        if (string.IsNullOrWhiteSpace(etag))
        {
            currentUser = await ReloadWritableUserAsync(currentUser.Id, cancellationToken);
            etag = currentUser.ETag;
        }

        try
        {
            await users.PatchStatsAsync(currentUser.Id, stats, clock.GetUtcNow(), etag!, cancellationToken);
            return;
        }
        catch (ProfileConflictException ex) when (ex.Code == "stats_conflict")
        {
            currentUser = await ReloadWritableUserAsync(currentUser.Id, cancellationToken);
        }

        try
        {
            await users.PatchStatsAsync(currentUser.Id, stats, clock.GetUtcNow(), currentUser.ETag!, cancellationToken);
        }
        catch (ProfileConflictException ex) when (ex.Code == "stats_conflict")
        {
            throw;
        }
    }

    private async Task<UserDocument> ReloadWritableUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new ProfileNotFoundException("profile_not_found", "Profile not found.");
        }

        if (user.DeletedAt is not null)
        {
            throw new ProfileAccessException("user_deleted", "User was deleted.");
        }

        if (string.IsNullOrWhiteSpace(user.ETag))
        {
            throw new ProfileConflictException("stats_conflict", "Missing user etag.");
        }

        return user;
    }
}
