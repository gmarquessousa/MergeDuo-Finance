using MergeDuo.Profile.Domain.Abstractions;
using MergeDuo.Profile.Domain;
using MergeDuo.Profile.Domain.Documents;
using MergeDuo.Profile.Domain.Exceptions;
using MergeDuo.Profile.Domain.Options;
using MergeDuo.Profile.Domain.Rules;

namespace MergeDuo.Profile.Domain.Services;

public sealed class ProfileQueryService(
    IUsersRepository users,
    IPartnershipsRepository partnerships,
    StatsOptions options,
    TimeProvider clock) : IProfileQueryService
{
    private TimeSpan StaleAfter => TimeSpan.FromMinutes(options.StaleAfterMinutes);

    public async Task<PublicProfileResponse?> GetProfileByIdAsync(
        UserDocument requester,
        string targetUserId,
        CancellationToken cancellationToken)
    {
        var target = await users.GetByIdAsync(targetUserId, cancellationToken);
        if (target is null || target.DeletedAt is not null)
        {
            return null;
        }

        return await MapAsync(requester, target, cancellationToken);
    }

    public async Task<PublicProfileResponse?> GetProfileByHandleAsync(
        UserDocument requester,
        string handle,
        CancellationToken cancellationToken)
    {
        var normalized = HandleRules.Normalize(handle);
        if (!HandleRules.IsValid(normalized))
        {
            throw new ArgumentException("Invalid handle.", nameof(handle));
        }

        var matches = (await users.FindByHandleAsync(normalized, cancellationToken))
            .Where(x => x.DeletedAt is null)
            .ToArray();

        if (matches.Length > 1)
        {
            throw new ProfileConflictException("duplicate_handle_detected", "Duplicate handle detected.");
        }

        var target = matches.SingleOrDefault();
        return target is null ? null : await MapAsync(requester, target, cancellationToken);
    }

    private async Task<PublicProfileResponse> MapAsync(
        UserDocument requester,
        UserDocument target,
        CancellationToken cancellationToken)
    {
        PartnershipDocument? partnership = null;
        if (requester.Id != target.Id)
        {
            partnership = await partnerships.GetRelationshipAsync(requester.Id, target.Id, cancellationToken);
        }

        var relationship = partnership.ToActiveRelationship();
        var includeStats = StatsRules.CanSeeStats(requester, target, partnership);
        return ProfileMapping.ToPublicProfile(
            target,
            includeStats,
            relationship,
            clock.GetUtcNow(),
            StaleAfter);
    }
}
