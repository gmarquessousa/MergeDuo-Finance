using MergeDuo.Identity.Domain.Documents;
using MergeDuo.Identity.Domain.Options;
using MergeDuo.Identity.Domain.Rules;
using MergeDuo.Identity.Infra.Security;

namespace MergeDuo.Identity.Tests;

public sealed class DomainRulesTests
{
    [Fact]
    public void Handle_candidates_normalize_name_and_increment()
    {
        var handles = HandleRules.Candidates("Gabriel Marques", "gabriel@example.com").Take(3).ToArray();

        Assert.Equal("@gabrielmarques", handles[0]);
        Assert.Equal("@gabrielmarques2", handles[1]);
        Assert.True(HandleRules.IsValid(HandleRules.Normalize("@GABRIEL_1")));
    }

    [Fact]
    public void Profile_patch_rejects_invalid_handle()
    {
        var errors = ProfilePatchRules.Validate("Gabriel", "Gabriel", null);

        Assert.Contains("handle", errors);
    }

    [Fact]
    public void Initials_are_derived_from_name_or_email()
    {
        Assert.Equal("GM", IdentityRules.Initials("Gabriel Marques", "g@example.com"));
        Assert.Equal("GA", IdentityRules.Initials("", "gabriel@example.com"));
    }

    [Fact]
    public void Soft_delete_replaces_unique_keys_with_tombstones()
    {
        var now = DateTimeOffset.Parse("2026-04-25T12:00:00Z");
        var user = IdentityRules.CreateUser(
            "usr_test",
            "@gabriel",
            "Gabriel Marques",
            "gabriel@example.com",
            "google-sub",
            null,
            null,
            now.AddDays(-1));

        IdentityRules.ApplySoftDelete(user, now);

        Assert.NotNull(user.DeletedAt);
        Assert.StartsWith("@deleted_", user.Handle);
        Assert.Equal("deleted+usr_test@deleted.mergeduo.local", user.Email);
        Assert.StartsWith("deleted:usr_test:", user.Auth.Google.Sub);
        Assert.Equal(user.Email, user.Auth.Google.Email);
    }

    [Fact]
    public void Identity_reservation_id_hashes_normalized_value()
    {
        var upper = IdentityReservationRules.For(IdentityReservationRules.KindHandle, " @Gabriel ");
        var lower = IdentityReservationRules.For(IdentityReservationRules.KindHandle, "@gabriel");

        Assert.Equal(upper.Id, lower.Id);
        Assert.StartsWith("idx_", upper.Id);
        Assert.DoesNotContain("gabriel", upper.Id, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64, upper.ValueHash.Length);
    }

    [Fact]
    public void Refresh_session_expires_and_revokes()
    {
        var now = DateTimeOffset.Parse("2026-04-25T12:00:00Z");
        var device = new DeviceDocument
        {
            RevokedAt = null,
            Session = new DeviceSession
            {
                SessionId = "ses_1",
                RefreshTokenHash = "hash",
                RefreshTokenExpiresAt = now.AddMinutes(1)
            }
        };

        Assert.True(IdentityRules.IsRefreshSessionActive(device, now));
        Assert.False(IdentityRules.IsRefreshSessionActive(device, now.AddMinutes(2)));

        IdentityRules.RevokeSession(device, now);

        Assert.NotNull(device.RevokedAt);
        Assert.Null(device.Session.RefreshTokenHash);
    }

    [Fact]
    public void Refresh_token_hash_uses_constant_time_comparison()
    {
        var protector = new RefreshTokenProtector(new RefreshTokenOptions { Pepper = "pepper" });
        var issued = protector.Issue("usr_1", "dev_1", "ses_1");

        Assert.NotEqual(issued.Token, issued.Hash);
        Assert.True(protector.FixedTimeEquals(issued.Token, issued.Hash));
        Assert.False(protector.FixedTimeEquals(issued.Token + "x", issued.Hash));
    }
}
