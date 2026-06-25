using MergeDuo.Partnership.Domain.Documents;
using MergeDuo.Partnership.Domain.Exceptions;
using MergeDuo.Partnership.Domain.Rules;

namespace MergeDuo.Partnership.Tests;

public sealed class DomainRulesTests
{
    [Fact]
    public void Generated_invite_token_is_valid_and_has_configured_entropy()
    {
        var token = InviteTokenRules.Generate(32);

        Assert.True(InviteTokenRules.IsValid(token));
        Assert.True(token.Length >= 43);
        Assert.DoesNotContain("+", token);
        Assert.DoesNotContain("/", token);
        Assert.DoesNotContain("=", token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("token with spaces")]
    public void Invalid_token_is_rejected(string token)
    {
        var ex = Assert.Throws<PartnershipBadRequestException>(() => InviteTokenRules.EnsureValid(token));

        Assert.Equal("invalid_invite_token", ex.Code);
    }

    [Fact]
    public void Partnership_ids_are_deterministic_and_ordered()
    {
        var first = PartnershipRules.PartnershipId("usr_b", "usr_a");
        var second = PartnershipRules.PartnershipId("usr_a", "usr_b");

        Assert.Equal("pair_usr_a_usr_b", first);
        Assert.Equal(first, second);
        Assert.Equal("pair_usr_a_usr_b_usr_a", PartnershipRules.DocumentId("usr_b", "usr_a", "usr_a"));
    }

    [Fact]
    public void Self_accept_is_blocked()
    {
        var invite = new MergeInviteDocument
        {
            InviterUserId = "usr_a",
            Status = InviteStatuses.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        var ex = Assert.Throws<PartnershipConflictException>(() =>
            InviteRules.EnsureCanAccept(invite, "usr_a", DateTimeOffset.UtcNow));

        Assert.Equal("self_invite_not_allowed", ex.Code);
    }

    [Fact]
    public void Pair_documents_cross_partner_snapshots_and_starting_balances()
    {
        var inviter = User("usr_a", "Ana Lima", "@ana", "AL", 100m);
        var accepter = User("usr_b", "Bia Lima", "@bia", "BL", 250m);

        var pair = PartnershipRules.BuildPair(inviter, accepter, DateTimeOffset.UtcNow);

        Assert.Equal("usr_b", pair.InviterDocument.PartnerSnapshot.UserId);
        Assert.Equal("usr_a", pair.AccepterDocument.PartnerSnapshot.UserId);
        Assert.Equal(100m, pair.InviterDocument.StartingBalance);
        Assert.Equal(250m, pair.AccepterDocument.StartingBalance);
    }

    private static UserSummaryDocument User(
        string id,
        string name,
        string handle,
        string initials,
        decimal startingBalance) =>
        new()
        {
            Id = id,
            Name = name,
            Handle = handle,
            AvatarInitials = initials,
            Financial = new UserFinancial { StartingBalance = startingBalance }
        };
}
