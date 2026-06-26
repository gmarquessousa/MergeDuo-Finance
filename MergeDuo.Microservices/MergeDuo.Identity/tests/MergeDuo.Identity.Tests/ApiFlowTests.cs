using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MergeDuo.Identity.Api;
using MergeDuo.Identity.Domain.Abstractions;
using MergeDuo.Identity.Domain.Documents;
using MergeDuo.Identity.Domain.Options;
using MergeDuo.Identity.Domain.Rules;
using MergeDuo.Identity.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace MergeDuo.Identity.Tests;

public sealed class ApiFlowTests
{
    [Fact]
    public async Task Challenge_and_callback_create_user_and_device()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();

        var login = await LoginAsync(client);

        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.Single(factory.Users.Users);
        Assert.Single(factory.Devices.Devices);
        Assert.Equal("@gabrielmarques", login.User.Handle);
        Assert.NotNull(login.User.Financial);
        Assert.NotNull(login.User.Stats);
        Assert.False(string.IsNullOrWhiteSpace(login.User.RegisteredAt));
        Assert.Equal(login.DeviceId, factory.Devices.Devices.Single().Id);
    }

    [Fact]
    public async Task Recurring_login_does_not_duplicate_user()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();

        await LoginAsync(client);
        await LoginAsync(client);

        Assert.Single(factory.Users.Users);
        Assert.Single(factory.Devices.Devices);
    }

    [Fact]
    public async Task Recurring_login_updates_google_snapshot_with_lightweight_patch()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();

        await LoginAsync(client);
        factory.Google.Principal = factory.Google.Principal with
        {
            Email = "gabriel.updated@gmail.com",
            PictureUrl = "https://lh3.googleusercontent.com/a/updated",
            HostedDomain = "example.com"
        };

        await LoginAsync(client);

        var user = factory.Users.Users.Single();
        Assert.Equal("gabriel.updated@gmail.com", user.Auth.Google.Email);
        Assert.Equal("https://lh3.googleusercontent.com/a/updated", user.Auth.Google.PictureUrl);
        Assert.Equal("example.com", user.Auth.Google.HostedDomain);
        Assert.Equal(1, factory.Users.LoginSnapshotUpdateCalls);
        Assert.Equal(0, factory.Users.GenericUpdateCalls);
    }

    [Fact]
    public async Task Callback_returns_dependency_unavailable_when_google_dependency_fails()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();
        factory.Google.ExceptionToThrow = new IdentityDependencyException("jwks unavailable");

        var response = await SendLoginAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("identity_dependency_unavailable", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task In_memory_repository_update_login_snapshot_updates_only_login_fields()
    {
        var repo = new InMemoryUsersRepository();
        var now = DateTimeOffset.UtcNow;
        var user = IdentityRules.CreateUser(
            "usr_test",
            "@test",
            "Test User",
            "test@example.com",
            "google-sub-test",
            null,
            null,
            now);
        await repo.CreateAsync(user, CancellationToken.None);

        await repo.UpdateLoginSnapshotAsync(
            user.Id,
            "updated@example.com",
            true,
            "https://lh3.googleusercontent.com/a/updated",
            "example.com",
            now.AddMinutes(1),
            CancellationToken.None);

        var updated = await repo.GetByIdAsync(user.Id, CancellationToken.None);
        Assert.Equal("updated@example.com", updated!.Auth.Google.Email);
        Assert.Equal("https://lh3.googleusercontent.com/a/updated", updated.Auth.Google.PictureUrl);
        Assert.Equal("example.com", updated.Auth.Google.HostedDomain);
        Assert.Equal(now.AddMinutes(1), updated.Auth.LastLoginAt);
        Assert.Equal(1, repo.LoginSnapshotUpdateCalls);
        Assert.Equal(0, repo.GenericUpdateCalls);
    }

    [Fact]
    public async Task Refresh_rotates_token_and_reuse_fails()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

        var challengeResponse = await client.GetAsync("/auth/google/challenge");
        challengeResponse.EnsureSuccessStatusCode();
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<GoogleChallengeResponse>())!;
        var challengeCookie = ExtractCookie(challengeResponse, HttpHelpers.ChallengeCookieName);

        var callback = new HttpRequestMessage(HttpMethod.Post, "/auth/google/callback")
        {
            Content = JsonContent.Create(LoginRequest())
        };
        callback.Headers.Add("x-csrf-token", challenge.CsrfToken);
        callback.Headers.Add("Cookie", $"{HttpHelpers.ChallengeCookieName}={challengeCookie}");
        var callbackResponse = await client.SendAsync(callback);
        callbackResponse.EnsureSuccessStatusCode();
        var auth = (await callbackResponse.Content.ReadFromJsonAsync<AuthTokenResponse>())!;
        var refreshCookieName = factory.Services.GetRequiredService<RefreshTokenOptions>().CookieName;
        var oldRefresh = ExtractCookie(callbackResponse, refreshCookieName);
        var oldCsrf = ExtractCookie(callbackResponse, HttpHelpers.CsrfCookieName);

        var refresh = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        refresh.Headers.Add("x-csrf-token", auth.CsrfToken);
        refresh.Headers.Add("Cookie", $"{refreshCookieName}={oldRefresh}; {HttpHelpers.CsrfCookieName}={oldCsrf}");
        var refreshResponse = await client.SendAsync(refresh);
        refreshResponse.EnsureSuccessStatusCode();
        var refreshed = (await refreshResponse.Content.ReadFromJsonAsync<AuthTokenResponse>())!;
        Assert.NotNull(refreshed.User.Financial);
        Assert.NotNull(refreshed.User.Stats);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.User.RegisteredAt));

        var reuse = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        reuse.Headers.Add("x-csrf-token", oldCsrf);
        reuse.Headers.Add("Cookie", $"{refreshCookieName}={oldRefresh}; {HttpHelpers.CsrfCookieName}={oldCsrf}");
        var reuseResponse = await client.SendAsync(reuse);

        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
        Assert.Equal("invalid_refresh_token", await ProblemCodeAsync(reuseResponse));
    }

    [Fact]
    public async Task Redirect_start_returns_signed_state_and_same_origin_login_uri()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/auth/google/redirect/start", new GoogleRedirectStartRequest(
            true,
            "/invites/inv_test",
            new DeviceRequest("browser-install", "web", "Mozilla/5.0", "iPhone", "iOS", "0.1.0")));

        response.EnsureSuccessStatusCode();
        var start = (await response.Content.ReadFromJsonAsync<GoogleRedirectStartResponse>())!;
        Assert.False(string.IsNullOrWhiteSpace(start.Nonce));
        Assert.False(string.IsNullOrWhiteSpace(start.State));
        var publicApp = factory.Services.GetRequiredService<PublicAppOptions>();
        var expectedBaseUrl = string.IsNullOrWhiteSpace(publicApp.BaseUrl)
            ? "https://localhost"
            : publicApp.BaseUrl.TrimEnd('/');
        Assert.Equal($"{expectedBaseUrl}/auth/google/redirect-callback", start.LoginUri);
        Assert.Equal(600, start.ExpiresIn);
    }

    [Fact]
    public async Task Redirect_callback_sets_cookies_and_refresh_can_restore_session()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();

        var start = await StartRedirectAsync(client, "/invites/inv_test");
        var callback = await SendRedirectCallbackAsync(client, start.State);

        Assert.Equal(HttpStatusCode.SeeOther, callback.StatusCode);
        var location = callback.Headers.Location?.OriginalString ?? "";
        Assert.StartsWith("/invites/inv_test#auth_redirect=1&", location);
        var csrf = FragmentValue(location, "csrf");
        Assert.False(string.IsNullOrWhiteSpace(csrf));
        Assert.Equal("1", FragmentValue(location, "remember"));

        var refresh = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        refresh.Headers.Add("x-csrf-token", csrf);
        var refreshResponse = await client.SendAsync(refresh);

        refreshResponse.EnsureSuccessStatusCode();
        var session = (await refreshResponse.Content.ReadFromJsonAsync<AuthTokenResponse>())!;
        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        Assert.Single(factory.Users.Users);
        Assert.Single(factory.Devices.Devices);
    }

    [Fact]
    public async Task Redirect_callback_rejects_invalid_state()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();

        var response = await SendRedirectCallbackAsync(client, "invalid-state");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Redirect_start_normalizes_external_return_path()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();

        var start = await StartRedirectAsync(client, "https://evil.example/path");
        var callback = await SendRedirectCallbackAsync(client, start.State);

        Assert.Equal(HttpStatusCode.SeeOther, callback.StatusCode);
        var location = callback.Headers.Location?.OriginalString ?? "";
        Assert.StartsWith("/#auth_redirect=1&", location);
    }

    [Fact]
    public async Task Refresh_without_csrf_fails()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();
        await LoginAsync(client);

        var response = await client.PostAsync("/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_refresh_token", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Revoked_device_cannot_refresh_or_access_users_me()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();
        var login = await LoginAsync(client);
        var device = factory.Devices.Devices.Single();
        IdentityRules.RevokeSession(device, DateTimeOffset.UtcNow);

        var refresh = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        refresh.Headers.Add("x-csrf-token", login.CsrfToken);
        var refreshResponse = await client.SendAsync(refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
        Assert.Equal("device_revoked", await ProblemCodeAsync(refreshResponse));

        var me = new HttpRequestMessage(HttpMethod.Get, "/users/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var meResponse = await client.SendAsync(me);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
        Assert.Equal("device_revoked", await ProblemCodeAsync(meResponse));
    }

    [Fact]
    public async Task Logout_revokes_current_device()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();
        var login = await LoginAsync(client);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var response = await client.SendAsync(logout);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(factory.Devices.Devices.Single().RevokedAt);
    }

    [Fact]
    public async Task Users_me_does_not_return_sensitive_fields()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();
        var login = await LoginAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Get, "/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.DoesNotContain("\"google\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"auth\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_etag", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avatarInitials", json);
    }

    [Fact]
    public async Task Users_me_returns_google_picture_url_when_no_custom_avatar()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();
        var login = await LoginAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Get, "/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var response = await client.SendAsync(request);
        var me = (await response.Content.ReadFromJsonAsync<UserMeResponse>())!;

        response.EnsureSuccessStatusCode();
        Assert.Equal("https://lh3.googleusercontent.com/a/test", me.AvatarUrl);
    }

    [Fact]
    public async Task Users_me_returns_custom_avatar_url_when_uploaded()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();
        var login = await LoginAsync(client);
        await SendAvatarAsync(client, login.AccessToken, "image/png", [1, 2, 3, 4]);

        var request = new HttpRequestMessage(HttpMethod.Get, "/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var response = await client.SendAsync(request);
        var me = (await response.Content.ReadFromJsonAsync<UserMeResponse>())!;

        response.EnsureSuccessStatusCode();
        Assert.StartsWith("https://stmergeduo.blob.core.windows.net/avatars/", me.AvatarUrl);
    }

    [Fact]
    public async Task Patch_users_me_maps_handle_conflict()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();
        var login = await LoginAsync(client);
        await factory.Users.CreateAsync(new UserDocument
        {
            Id = "usr_other",
            Name = "Other",
            Handle = "@taken",
            Email = "other@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Auth = new UserAuth { Google = new GoogleAuthState { Sub = "other-sub" } }
        }, CancellationToken.None);

        var patch = new HttpRequestMessage(HttpMethod.Patch, "/users/me")
        {
            Content = JsonContent.Create(new PatchUserMeRequest(null, "@taken", null, null))
        };
        patch.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var response = await client.SendAsync(patch);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("handle_already_taken", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Patch_users_me_updates_reservations_and_blocks_released_handle()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();
        var login = await LoginAsync(client);

        var patch = new HttpRequestMessage(HttpMethod.Patch, "/users/me")
        {
            Content = JsonContent.Create(new PatchUserMeRequest(null, "@newhandle", null, null))
        };
        patch.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var response = await client.SendAsync(patch);

        response.EnsureSuccessStatusCode();
        Assert.Contains(factory.Users.Reservations, x => x.Status == IdentityReservationRules.StatusReleased);
        Assert.Contains(factory.Users.Reservations, x => x.Status == IdentityReservationRules.StatusActive && x.UserId == factory.Users.Users.Single().Id);

        var oldHandleUser = new UserDocument
        {
            Id = "usr_other",
            Name = "Other",
            Handle = "@gabrielmarques",
            Email = "other@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Auth = new UserAuth { Google = new GoogleAuthState { Sub = "other-sub" } }
        };
        await Assert.ThrowsAsync<IdentityConflictException>(() => factory.Users.CreateAsync(oldHandleUser, CancellationToken.None));
    }

    [Fact]
    public async Task Avatar_validates_type_size_and_deletes_old_blob_after_replacement()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();
        var login = await LoginAsync(client);

        var badType = await SendAvatarAsync(client, login.AccessToken, "text/plain", Encoding.UTF8.GetBytes("not-image"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, badType.StatusCode);

        var tooLarge = await SendAvatarAsync(client, login.AccessToken, "image/png", new byte[2 * 1024 * 1024 + 1]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);

        var user = factory.Users.Users.Single();
        user.AvatarUrl = "https://stmergeduo.blob.core.windows.net/avatars/usr_old/old.png";
        var ok = await SendAvatarAsync(client, login.AccessToken, "image/png", [1, 2, 3, 4]);

        ok.EnsureSuccessStatusCode();
        Assert.Contains("old.png", factory.AvatarStorage.DeletedUrls.Single());
        Assert.StartsWith("https://stmergeduo.blob.core.windows.net/avatars/", factory.Users.Users.Single().AvatarUrl);
    }

    [Fact]
    public async Task Delete_users_me_soft_deletes_user_and_revokes_devices()
    {
        using var factory = new TestIdentityFactory();
        using var client = factory.CreateHttpsClient();
        var login = await LoginAsync(client);

        var delete = new HttpRequestMessage(HttpMethod.Delete, "/users/me");
        delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var response = await client.SendAsync(delete);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var user = factory.Users.Users.Single();
        Assert.NotNull(user.DeletedAt);
        Assert.StartsWith("@deleted_", user.Handle);
        Assert.NotNull(factory.Devices.Devices.Single().RevokedAt);

        var me = new HttpRequestMessage(HttpMethod.Get, "/users/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var meResponse = await client.SendAsync(me);
        Assert.Equal(HttpStatusCode.Forbidden, meResponse.StatusCode);
        Assert.Equal("user_deleted", await ProblemCodeAsync(meResponse));
    }

    private static async Task<AuthTokenResponse> LoginAsync(HttpClient client)
    {
        var response = await SendLoginAsync(client);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokenResponse>())!;
    }

    private static async Task<HttpResponseMessage> SendLoginAsync(HttpClient client)
    {
        var challenge = (await client.GetFromJsonAsync<GoogleChallengeResponse>("/auth/google/challenge"))!;
        var callback = new HttpRequestMessage(HttpMethod.Post, "/auth/google/callback")
        {
            Content = JsonContent.Create(LoginRequest())
        };
        callback.Headers.Add("x-csrf-token", challenge.CsrfToken);
        return await client.SendAsync(callback);
    }

    private static async Task<GoogleRedirectStartResponse> StartRedirectAsync(HttpClient client, string returnPath)
    {
        var response = await client.PostAsJsonAsync("/auth/google/redirect/start", new GoogleRedirectStartRequest(
            true,
            returnPath,
            new DeviceRequest("browser-install", "web", "Mozilla/5.0", "iPhone", "iOS", "0.1.0")));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GoogleRedirectStartResponse>())!;
    }

    private static Task<HttpResponseMessage> SendRedirectCallbackAsync(HttpClient client, string state) =>
        client.PostAsync("/auth/google/redirect-callback", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("credential", "fake-google-token"),
            new KeyValuePair<string, string>("state", state)
        ]));

    private static GoogleCallbackRequest LoginRequest() =>
        new(
            "fake-google-token",
            true,
            new DeviceRequest(
                "browser-install",
                "web",
                "Mozilla/5.0",
                "Desktop",
                "Windows 11",
                "0.1.0"));

    private static async Task<HttpResponseMessage> SendAvatarAsync(
        HttpClient client,
        string accessToken,
        string contentType,
        byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "avatar", "avatar.bin");
        var request = new HttpRequestMessage(HttpMethod.Post, "/users/me/avatar") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request);
    }

    private static string ExtractCookie(HttpResponseMessage response, string name)
    {
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : [];
        var setCookie = setCookies.FirstOrDefault(x => x.StartsWith(name + "=", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Missing Set-Cookie for {name}. Headers: {string.Join(" | ", setCookies)}");
        return setCookie[(name.Length + 1)..].Split(';', 2)[0];
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string FragmentValue(string location, string name)
    {
        var fragment = location.Split('#', 2).ElementAtOrDefault(1) ?? "";
        foreach (var part in fragment.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == name)
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return "";
    }
}
