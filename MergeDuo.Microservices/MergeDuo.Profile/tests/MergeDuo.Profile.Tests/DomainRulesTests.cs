using System.Text.Json;
using MergeDuo.Profile.Domain.Documents;
using MergeDuo.Profile.Domain.Options;
using MergeDuo.Profile.Domain.Rules;
using MergeDuo.Profile.Domain.Services;
using MergeDuo.Profile.Tests.Fakes;
using MergeDuo.Profile.Tests.Support;

namespace MergeDuo.Profile.Tests;

public sealed class DomainRulesTests
{
    [Theory]
    [InlineData("Gabriel_1", "@gabriel_1")]
    [InlineData("@GABRIEL.1", "@gabriel.1")]
    [InlineData("  bmarques  ", "@bmarques")]
    public void Handle_normalizes_with_optional_at(string input, string expected)
    {
        var normalized = HandleRules.Normalize(input);

        Assert.Equal(expected, normalized);
        Assert.True(HandleRules.IsValid(normalized));
    }

    [Theory]
    [InlineData("@a")]
    [InlineData("gabriel")]
    [InlineData("@invalid-hyphen")]
    [InlineData("@abcdefghijklmnopqrstuvwxyz12345")]
    public void Handle_rejects_invalid_format(string handle)
    {
        Assert.False(HandleRules.IsValid(handle));
    }

    [Theory]
    [InlineData("usr_gmarques", true)]
    [InlineData("usr_ABC-123_", true)]
    [InlineData("user_gmarques", false)]
    [InlineData("usr_", false)]
    public void User_id_validates_expected_prefix(string userId, bool expected)
    {
        Assert.Equal(expected, UserIdRules.IsValid(userId));
    }

    [Fact]
    public void Public_mapping_does_not_include_sensitive_fields()
    {
        var user = User("usr_gmarques", "@gmarques");

        var response = ProfileMapping.ToPublicProfile(
            user,
            includeStats: true,
            relationship: null,
            DateTimeOffset.Parse("2026-04-27T14:00:00Z"),
            TimeSpan.FromHours(1));
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("avatarInitials", json);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("financial", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preferences", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auth", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_etag", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Staleness_uses_missing_or_old_last_recomputed_at()
    {
        var now = DateTimeOffset.Parse("2026-04-27T14:00:00Z");

        Assert.True(StatsRules.IsStale(null, now, TimeSpan.FromHours(1)));
        Assert.True(StatsRules.IsStale(new UserStats(), now, TimeSpan.FromHours(1)));
        Assert.True(StatsRules.IsStale(new UserStats { LastRecomputedAt = now.AddMinutes(-61) }, now, TimeSpan.FromHours(1)));
        Assert.False(StatsRules.IsStale(new UserStats { LastRecomputedAt = now.AddMinutes(-10) }, now, TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task Profile_stats_are_visible_only_to_self_or_active_partner()
    {
        var users = new InMemoryUsersRepository();
        var partnerships = new InMemoryPartnershipsRepository();
        var requester = User("usr_gmarques", "@gmarques");
        var target = User("usr_bmarques", "@bmarques");
        users.Add(requester);
        users.Add(target);

        var service = new ProfileQueryService(
            users,
            partnerships,
            new StatsOptions(),
            new TestClock(DateTimeOffset.Parse("2026-04-27T14:00:00Z")));

        var withoutRelationship = await service.GetProfileByIdAsync(requester, target.Id, CancellationToken.None);
        Assert.Null(withoutRelationship!.Stats);
        Assert.Null(withoutRelationship.Relationship);

        partnerships.Add(new PartnershipDocument
        {
            UserId = requester.Id,
            PartnerUserId = target.Id,
            Status = "paused",
            MergedSince = "2026-01-12"
        });
        var paused = await service.GetProfileByIdAsync(requester, target.Id, CancellationToken.None);
        Assert.Null(paused!.Stats);
        Assert.Null(paused.Relationship);

        partnerships.Add(new PartnershipDocument
        {
            UserId = requester.Id,
            PartnerUserId = target.Id,
            Status = "active",
            MergedSince = "2026-01-12"
        });
        var active = await service.GetProfileByIdAsync(requester, target.Id, CancellationToken.None);
        Assert.NotNull(active!.Stats);
        Assert.Equal("active", active.Relationship!.Status);
    }

    private static UserDocument User(string id, string handle) =>
        new()
        {
            Id = id,
            Name = id,
            Handle = handle,
            Email = $"{id}@example.com",
            Phone = "+55 11 99999-9999",
            AvatarInitials = "GM",
            MemberSince = "Jan 2026",
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            Stats = new UserStats
            {
                TransactionsTracked = 10,
                ActiveMonths = 2,
                LastRecomputedAt = DateTimeOffset.Parse("2026-04-27T13:30:00Z")
            }
        };
}
