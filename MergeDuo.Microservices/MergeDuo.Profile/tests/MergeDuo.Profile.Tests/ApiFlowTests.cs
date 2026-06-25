using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MergeDuo.Profile.Domain;
using MergeDuo.Profile.Domain.Documents;

namespace MergeDuo.Profile.Tests;

public sealed class ApiFlowTests
{
    [Fact]
    public async Task Protected_endpoint_rejects_missing_jwt()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/users/usr_gmarques");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Soft_deleted_requester_gets_403()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        requester.DeletedAt = DateTimeOffset.UtcNow;
        factory.Users.Add(requester);

        var response = await SendAsync(client, HttpMethod.Get, "/users/usr_gmarques", factory.IssueToken(requester.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("user_deleted", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Users_by_id_returns_public_profile_without_sensitive_fields()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        factory.Users.Add(requester);

        var response = await SendAsync(client, HttpMethod.Get, "/users/usr_gmarques", factory.IssueToken(requester.Id));
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Contains("avatarInitials", json);
        Assert.Contains("stats", json);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("financial", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preferences", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auth", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_etag", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Users_by_id_hides_stats_without_active_relationship()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        var target = User("usr_bmarques", "@bmarques");
        factory.Users.Add(requester);
        factory.Users.Add(target);

        var response = await SendAsync(client, HttpMethod.Get, "/users/usr_bmarques", factory.IssueToken(requester.Id));
        response.EnsureSuccessStatusCode();
        var profile = (await response.Content.ReadFromJsonAsync<PublicProfileResponse>())!;

        Assert.Null(profile.Stats);
        Assert.Null(profile.Relationship);
    }

    [Fact]
    public async Task Users_by_id_includes_relationship_and_stats_for_active_partner()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        var target = User("usr_bmarques", "@bmarques");
        factory.Users.Add(requester);
        factory.Users.Add(target);
        factory.Partnerships.Add(new PartnershipDocument
        {
            UserId = requester.Id,
            PartnerUserId = target.Id,
            Status = "active",
            MergedSince = "2026-01-12"
        });

        var response = await SendAsync(client, HttpMethod.Get, "/users/usr_bmarques", factory.IssueToken(requester.Id));
        response.EnsureSuccessStatusCode();
        var profile = (await response.Content.ReadFromJsonAsync<PublicProfileResponse>())!;

        Assert.NotNull(profile.Stats);
        Assert.Equal("active", profile.Relationship!.Status);
        Assert.Equal("2026-01-12", profile.Relationship.MergedSince);
    }

    [Fact]
    public async Task Missing_or_deleted_profile_returns_404()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        var deleted = User("usr_deleted", "@deleted");
        deleted.DeletedAt = DateTimeOffset.UtcNow;
        factory.Users.Add(requester);
        factory.Users.Add(deleted);

        var missing = await SendAsync(client, HttpMethod.Get, "/users/usr_missing", factory.IssueToken(requester.Id));
        var removed = await SendAsync(client, HttpMethod.Get, "/users/usr_deleted", factory.IssueToken(requester.Id));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("profile_not_found", await ProblemCodeAsync(missing));
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Equal("profile_not_found", await ProblemCodeAsync(removed));
    }

    [Fact]
    public async Task Invalid_user_id_returns_400()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        factory.Users.Add(requester);

        var response = await SendAsync(client, HttpMethod.Get, "/users/not-valid", factory.IssueToken(requester.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_user_id", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Lookup_by_handle_accepts_without_at_and_normalizes()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        var target = User("usr_bmarques", "@bmarques");
        factory.Users.Add(requester);
        factory.Users.Add(target);

        var response = await SendAsync(client, HttpMethod.Get, "/users/by-handle/BMARQUES", factory.IssueToken(requester.Id));
        response.EnsureSuccessStatusCode();
        var profile = (await response.Content.ReadFromJsonAsync<PublicProfileResponse>())!;

        Assert.Equal("@bmarques", profile.Handle);
    }

    [Fact]
    public async Task Lookup_by_handle_detects_active_duplicates()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        var first = User("usr_a", "@dupe");
        var second = User("usr_b", "@dupe");
        factory.Users.Add(requester);
        factory.Users.Add(first);
        factory.Users.Add(second);

        var response = await SendAsync(client, HttpMethod.Get, "/users/by-handle/dupe", factory.IssueToken(requester.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("duplicate_handle_detected", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Me_stats_returns_cache_and_marks_stale()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        requester.Stats!.LastRecomputedAt = DateTimeOffset.UtcNow.AddHours(-2);
        factory.Users.Add(requester);

        var response = await SendAsync(client, HttpMethod.Get, "/me/stats", factory.IssueToken(requester.Id));
        response.EnsureSuccessStatusCode();
        var stats = (await response.Content.ReadFromJsonAsync<UserStatsResponse>())!;

        Assert.Equal("cache", stats.Source);
        Assert.True(stats.IsStale);
        Assert.Equal(0, factory.Users.PatchAttempts);
    }

    [Fact]
    public async Task Me_stats_recomputes_when_missing_or_fresh_requested()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        requester.Stats = null;
        factory.Users.Add(requester);
        factory.Transactions.TrackedCount = 128;
        factory.Transactions.ActiveMonths = ["2026-04", "2026-03", "2026-04"];

        var missingResponse = await SendAsync(client, HttpMethod.Get, "/me/stats", factory.IssueToken(requester.Id));
        missingResponse.EnsureSuccessStatusCode();
        var missingStats = (await missingResponse.Content.ReadFromJsonAsync<UserStatsResponse>())!;

        Assert.Equal("recomputed", missingStats.Source);
        Assert.Equal(128, missingStats.TransactionsTracked);
        Assert.Equal(2, missingStats.ActiveMonths);
        Assert.Equal(1, factory.Users.PatchAttempts);

        factory.Transactions.TrackedCount = 200;
        var freshResponse = await SendAsync(client, HttpMethod.Get, "/me/stats?fresh=true", factory.IssueToken(requester.Id));
        freshResponse.EnsureSuccessStatusCode();
        var freshStats = (await freshResponse.Content.ReadFromJsonAsync<UserStatsResponse>())!;

        Assert.Equal("recomputed", freshStats.Source);
        Assert.Equal(200, freshStats.TransactionsTracked);
        Assert.Equal(2, factory.Users.PatchAttempts);
    }

    [Fact]
    public async Task Me_stats_retries_once_on_etag_conflict()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        factory.Users.Add(requester);
        factory.Users.StatsConflictsBeforeSuccess = 1;
        factory.Transactions.TrackedCount = 7;

        var response = await SendAsync(client, HttpMethod.Get, "/me/stats?fresh=true", factory.IssueToken(requester.Id));

        response.EnsureSuccessStatusCode();
        Assert.Equal(2, factory.Users.PatchAttempts);
        Assert.Equal(7, requester.Stats!.TransactionsTracked);
    }

    [Fact]
    public async Task Me_stats_returns_409_when_retry_conflicts()
    {
        using var factory = new TestProfileFactory();
        using var client = factory.CreateHttpsClient();
        var requester = User("usr_gmarques", "@gmarques");
        factory.Users.Add(requester);
        factory.Users.StatsConflictsBeforeSuccess = 2;

        var response = await SendAsync(client, HttpMethod.Get, "/me/stats?fresh=true", factory.IssueToken(requester.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("stats_conflict", await ProblemCodeAsync(response));
        Assert.Equal(2, factory.Users.PatchAttempts);
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

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static UserDocument User(string id, string handle) =>
        new()
        {
            Id = id,
            Name = id == "usr_bmarques" ? "Bruna Marques" : "Gabriel Marques",
            Handle = handle,
            Email = $"{id}@example.com",
            Phone = "+55 11 99999-9999",
            AvatarInitials = id == "usr_bmarques" ? "BM" : "GM",
            AvatarUrl = "https://stmergeduo.blob.core.windows.net/avatars/test.jpg",
            MemberSince = "Jan 2026",
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            Stats = new UserStats
            {
                TransactionsTracked = 10,
                ActiveMonths = 2,
                LastRecomputedAt = DateTimeOffset.UtcNow
            }
        };
}
