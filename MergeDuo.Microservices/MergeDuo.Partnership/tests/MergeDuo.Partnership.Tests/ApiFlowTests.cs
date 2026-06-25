using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MergeDuo.Partnership.Domain.Contracts;
using MergeDuo.Partnership.Domain.Documents;
using MergeDuo.Partnership.Domain.Rules;

namespace MergeDuo.Partnership.Tests;

public sealed class ApiFlowTests
{
    [Fact]
    public async Task Protected_endpoint_rejects_missing_jwt()
    {
        using var factory = new TestPartnershipFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/invites", new CreateInviteRequest("link"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Soft_deleted_requester_gets_403()
    {
        using var factory = new TestPartnershipFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        requester.DeletedAt = DateTimeOffset.UtcNow;
        factory.Users.Add(requester);

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/invites",
            factory.IssueToken(requester.Id),
            new CreateInviteRequest("link"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("user_deleted", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Post_invites_creates_and_reuses_pending_invite()
    {
        using var factory = new TestPartnershipFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        factory.Users.Add(requester);

        var first = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/invites",
            factory.IssueToken(requester.Id),
            new CreateInviteRequest("link"));
        var firstInvite = (await first.Content.ReadFromJsonAsync<CreateInviteResponse>())!;

        var second = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/invites",
            factory.IssueToken(requester.Id),
            new CreateInviteRequest("qr"));
        var secondInvite = (await second.Content.ReadFromJsonAsync<CreateInviteResponse>())!;

        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.Equal(firstInvite.Token, secondInvite.Token);
        Assert.Equal("https://app.test/invites/" + firstInvite.Token, firstInvite.InviteUrl);
        Assert.Single(factory.Invites.All);
    }

    [Fact]
    public async Task Post_invites_rejects_current_partnership()
    {
        using var factory = new TestPartnershipFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        factory.Users.Add(requester);
        factory.Partnerships.Add(Partnership("usr_gmarques", "usr_bmarques", "active"));

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/invites",
            factory.IssueToken(requester.Id),
            new CreateInviteRequest("link"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("partnership_already_exists", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Get_invite_preview_is_safe()
    {
        using var factory = new TestPartnershipFactory();
        using var client = factory.CreateHttpsClient();
        var inviter = User("usr_gmarques", "@gmarques");
        factory.Users.Add(inviter);
        var invite = Invite(inviter);
        factory.Invites.Add(invite);

        var response = await client.GetAsync($"/invites/{invite.Token}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, json);
        Assert.Contains("inviter", json);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acceptedBy", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("partnershipId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_etag", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("financial", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_invite_preview_returns_expected_lifecycle_errors()
    {
        using var factory = new TestPartnershipFactory();
        using var client = factory.CreateHttpsClient();
        var inviter = User("usr_gmarques", "@gmarques");
        var expired = Invite(inviter, "expired_token_123456789012345", InviteStatuses.Pending);
        expired.ExpiresAt = factory.Clock.GetUtcNow().AddMinutes(-1);
        var revoked = Invite(inviter, "revoked_token_123456789012345", InviteStatuses.Revoked);
        factory.Invites.Add(expired);
        factory.Invites.Add(revoked);

        var expiredResponse = await client.GetAsync($"/invites/{expired.Token}");
        var revokedResponse = await client.GetAsync($"/invites/{revoked.Token}");

        Assert.Equal(HttpStatusCode.Gone, expiredResponse.StatusCode);
        Assert.Equal("invite_expired", await ProblemCodeAsync(expiredResponse));
        Assert.Equal(HttpStatusCode.Gone, revokedResponse.StatusCode);
        Assert.Equal("invite_revoked", await ProblemCodeAsync(revokedResponse));
    }

    [Fact]
    public async Task Accept_invite_creates_mirrored_partnerships_and_is_idempotent()
    {
        using var factory = new TestPartnershipFactory();
        using var client = factory.CreateHttpsClient();
        var inviter = User("usr_gmarques", "@gmarques", 100m);
        var accepter = User("usr_bmarques", "@bmarques", 200m);
        factory.Users.Add(inviter);
        factory.Users.Add(accepter);
        var invite = Invite(inviter);
        factory.Invites.Add(invite);

        var first = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/invites/{invite.Token}/accept",
            factory.IssueToken(accepter.Id),
            new { });
        var firstResponse = (await first.Content.ReadFromJsonAsync<AcceptInviteResponse>())!;

        var second = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/invites/{invite.Token}/accept",
            factory.IssueToken(accepter.Id),
            new { });
        var secondResponse = (await second.Content.ReadFromJsonAsync<AcceptInviteResponse>())!;

        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.Equal(firstResponse.PartnershipDocumentId, secondResponse.PartnershipDocumentId);
        Assert.Equal(2, factory.Partnerships.All.Count);
        Assert.Equal(-1, factory.Invites.All.Single().Ttl);
        Assert.Contains(factory.Partnerships.All, x => x.UserId == inviter.Id && x.PartnerSnapshot.UserId == accepter.Id && x.StartingBalance == 100m);
        Assert.Contains(factory.Partnerships.All, x => x.UserId == accepter.Id && x.PartnerSnapshot.UserId == inviter.Id && x.StartingBalance == 200m);
    }

    [Fact]
    public async Task Accept_by_another_user_after_accept_returns_conflict()
    {
        using var factory = new TestPartnershipFactory();
        using var client = factory.CreateHttpsClient();
        var inviter = User("usr_gmarques", "@gmarques");
        var accepter = User("usr_bmarques", "@bmarques");
        var other = User("usr_other", "@other");
        factory.Users.Add(inviter);
        factory.Users.Add(accepter);
        factory.Users.Add(other);
        var invite = Invite(inviter);
        factory.Invites.Add(invite);

        var accepted = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/invites/{invite.Token}/accept",
            factory.IssueToken(accepter.Id),
            new { });
        accepted.EnsureSuccessStatusCode();

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/invites/{invite.Token}/accept",
            factory.IssueToken(other.Id),
            new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invite_already_accepted", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Current_partnership_pause_and_end_update_mirrors_idempotently()
    {
        using var factory = new TestPartnershipFactory();
        using var client = factory.CreateHttpsClient();
        var user = User("usr_gmarques", "@gmarques");
        var partner = User("usr_bmarques", "@bmarques");
        factory.Users.Add(user);
        factory.Users.Add(partner);
        var owner = Partnership(user.Id, partner.Id, PartnershipStatuses.Active);
        var mirror = Partnership(partner.Id, user.Id, PartnershipStatuses.Active);
        factory.Partnerships.Add(owner);
        factory.Partnerships.Add(mirror);

        var current = await SendAsync(client, HttpMethod.Get, "/partnerships/me", factory.IssueToken(user.Id));
        var currentBody = (await current.Content.ReadFromJsonAsync<CurrentPartnershipResponse>())!;

        var pause = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/partnerships/{owner.Id}/pause",
            factory.IssueToken(user.Id),
            new { });
        var pauseAgain = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/partnerships/{owner.Id}/pause",
            factory.IssueToken(user.Id),
            new { });
        var end = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/partnerships/{owner.Id}/end",
            factory.IssueToken(user.Id),
            new { });
        var endAgain = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/partnerships/{owner.Id}/end",
            factory.IssueToken(user.Id),
            new { });

        Assert.True(current.IsSuccessStatusCode, await current.Content.ReadAsStringAsync());
        Assert.Equal(owner.Id, currentBody.Partnership!.Id);
        Assert.Equal(PartnershipStatuses.Paused, (await pause.Content.ReadFromJsonAsync<PartnershipStatusResponse>())!.Status);
        Assert.Equal(HttpStatusCode.OK, pauseAgain.StatusCode);
        Assert.Equal(PartnershipStatuses.Ended, (await end.Content.ReadFromJsonAsync<PartnershipStatusResponse>())!.Status);
        Assert.Equal(HttpStatusCode.OK, endAgain.StatusCode);
        Assert.All(factory.Partnerships.All, x => Assert.Equal(PartnershipStatuses.Ended, x.Status));
    }

    [Fact]
    public async Task Pause_returns_503_when_mirror_is_missing()
    {
        using var factory = new TestPartnershipFactory();
        using var client = factory.CreateHttpsClient();
        var user = User("usr_gmarques", "@gmarques");
        var partner = User("usr_bmarques", "@bmarques");
        factory.Users.Add(user);
        factory.Users.Add(partner);
        var owner = Partnership(user.Id, partner.Id, PartnershipStatuses.Active);
        factory.Partnerships.Add(owner);

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/partnerships/{owner.Id}/pause",
            factory.IssueToken(user.Id),
            new { });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("partnership_mirror_missing", await ProblemCodeAsync(response));
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        string token,
        T body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static UserSummaryDocument User(string id, string handle, decimal startingBalance = 0m) =>
        new()
        {
            Id = id,
            Name = id == "usr_bmarques" ? "Bruna Marques" : "Gabriel Marques",
            Handle = handle,
            AvatarInitials = id == "usr_bmarques" ? "BM" : "GM",
            Financial = new UserFinancial { StartingBalance = startingBalance }
        };

    private static MergeInviteDocument Invite(
        UserSummaryDocument inviter,
        string token = "valid_token_12345678901234567890",
        string status = InviteStatuses.Pending) =>
        new()
        {
            Id = $"invite_{Guid.NewGuid():N}",
            Token = token,
            InviterUserId = inviter.Id,
            Channel = "link",
            Status = status,
            InviterSnapshot = PartnershipRules.Snapshot(inviter),
            CreatedAt = DateTimeOffset.Parse("2026-04-27T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-04-27T12:00:00Z"),
            ExpiresAt = DateTimeOffset.Parse("2026-04-28T12:00:00Z")
        };

    private static PartnershipDocument Partnership(string userId, string partnerUserId, string status)
    {
        var now = DateTimeOffset.Parse("2026-04-27T12:00:00Z");
        return new PartnershipDocument
        {
            Id = PartnershipRules.DocumentId(userId, partnerUserId, userId),
            PartnershipId = PartnershipRules.PartnershipId(userId, partnerUserId),
            UserId = userId,
            PartnerUserId = partnerUserId,
            Status = status,
            PartnerSnapshot = new PartnerSnapshot
            {
                UserId = partnerUserId,
                Name = partnerUserId,
                Handle = $"@{partnerUserId}",
                Initials = "MD"
            },
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
