using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MergeDuo.Identity.Api;
using MergeDuo.Identity.Domain.Abstractions;
using MergeDuo.Identity.Domain.Documents;
using MergeDuo.Identity.Domain.Options;
using MergeDuo.Identity.Domain.Rules;
using MergeDuo.Identity.Infra.Cosmos;
using MergeDuo.Identity.Infra.Security;
using MergeDuo.Identity.Infra.Storage;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Azure.Cosmos;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

var cosmosOptions = Bind<CosmosOptions>("Cosmos");
var googleOptions = Bind<GoogleOptions>("Google");
var jwtOptions = Bind<JwtOptions>("Jwt");
var refreshOptions = Bind<RefreshTokenOptions>("RefreshTokens");
var publicAppOptions = Bind<PublicAppOptions>("PublicApp");
var blobOptions = Bind<BlobStorageOptions>("BlobStorage");
var corsOptions = Bind<CorsOptions>("Cors");

NormalizeSecrets(builder.Environment, jwtOptions, refreshOptions);

builder.Services.AddSingleton(cosmosOptions);
builder.Services.AddSingleton(googleOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(refreshOptions);
builder.Services.AddSingleton(publicAppOptions);
builder.Services.AddSingleton(blobOptions);
builder.Services.AddSingleton(corsOptions);
builder.Services.AddSingleton<IdentityMetrics>();
builder.Services.AddSingleton<IRefreshTokenProtector, RefreshTokenProtector>();
builder.Services.AddSingleton<IJwtIssuer, JwtIssuer>();
builder.Services.AddSingleton<CosmosClient>(sp => CosmosClientFactory.Create(sp.GetRequiredService<CosmosOptions>()));
builder.Services.AddSingleton<IUsersRepository, UsersRepository>();
builder.Services.AddSingleton<IDevicesRepository, DevicesRepository>();
builder.Services.AddSingleton<IAvatarStorage, BlobAvatarStorage>();
builder.Services.AddHttpClient("google-jwks", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<IGoogleIdTokenValidator>(sp => new GoogleIdTokenValidator(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("google-jwks"),
    sp.GetRequiredService<GoogleOptions>(),
    sp.GetRequiredService<TimeProvider>()));

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MergeDuo Identity API",
        Version = "v1",
        Description = "Identity, Google authentication, refresh tokens, profile and avatar endpoints for MergeDuo."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste only the JWT access token returned by /auth/google/callback or /auth/refresh."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            []
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = corsOptions.AllowedOrigins.Length == 0
            ? ["http://localhost:5173"]
            : corsOptions.AllowedOrigins;
        policy.WithOrigins(origins)
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("public-auth", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var publicJwtKey = JwtKeyFactory.CreatePublicKey(jwtOptions.PrivateKeyPem, jwtOptions.KeyId);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = publicJwtKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "userId"
        };
    });
builder.Services.AddAuthorization();

var applicationInsightsConnectionString =
    builder.Configuration["ApplicationInsights:ConnectionString"]
    ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

if (!builder.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(options => options.ConnectionString = applicationInsightsConnectionString)
        .WithMetrics(metrics => metrics.AddMeter(IdentityMetrics.MeterName));
}

var app = builder.Build();

if (cosmosOptions.AutoCreateContainers)
{
    await CosmosContainerInitializer.EnsureCreatedAsync(
        app.Services.GetRequiredService<CosmosClient>(),
        cosmosOptions,
        app.Lifetime.ApplicationStopping);
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MergeDuo Identity API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz");
app.MapGet("/readyz", async (
    CosmosClient client,
    CosmosOptions options,
    CancellationToken cancellationToken) =>
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

    try
    {
        await client.GetContainer(options.Database, options.UsersContainer).ReadContainerAsync(cancellationToken: linked.Token);
        await client.GetContainer(options.Database, options.DevicesContainer).ReadContainerAsync(cancellationToken: linked.Token);
        await client.GetContainer(options.Database, options.IdentityReservationsContainer).ReadContainerAsync(cancellationToken: linked.Token);
        return Results.Ok(new { status = "ready" });
    }
    catch
    {
        return ProblemHttp.DependencyUnavailable();
    }
});

var auth = app.MapGroup("/auth").WithTags("Auth");
auth.MapGet("/google/challenge", (
    HttpResponse response,
    RefreshTokenOptions refresh,
    TimeProvider clock) =>
{
    var nonce = HttpHelpers.NewSecret();
    var csrf = HttpHelpers.NewSecret();
    var now = clock.GetUtcNow();
    const int challengeTtlSeconds = 600;
    var expiresAt = now.AddSeconds(challengeTtlSeconds);
    var token = HttpHelpers.CreateChallengeCookie(nonce, csrf, expiresAt, refresh.Pepper);
    response.Cookies.Append(HttpHelpers.ChallengeCookieName, token, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = HttpHelpers.CookieSameSite(refresh),
        Path = "/auth",
        Expires = expiresAt
    });

    return Results.Ok(new GoogleChallengeResponse(nonce, csrf, challengeTtlSeconds, token));
}).RequireRateLimiting("public-auth");

auth.MapPost("/google/redirect/start", (
    GoogleRedirectStartRequest request,
    HttpRequest http,
    PublicAppOptions publicApp,
    RefreshTokenOptions refresh,
    TimeProvider clock) =>
{
    if (string.IsNullOrWhiteSpace(request.Device.InstallId)
        || string.IsNullOrWhiteSpace(request.Device.Platform))
    {
        return ProblemHttp.InvalidRequest();
    }

    var nonce = HttpHelpers.NewSecret();
    var csrf = HttpHelpers.NewSecret();
    var now = clock.GetUtcNow();
    const int challengeTtlSeconds = 600;
    var expiresAt = now.AddSeconds(challengeTtlSeconds);
    var returnPath = NormalizeReturnPath(request.ReturnPath);
    var state = HttpHelpers.CreateRedirectState(
        nonce,
        csrf,
        request.RememberMe,
        returnPath,
        request.Device,
        expiresAt,
        refresh.Pepper);
    var loginUri = $"{PublicBaseUrl(http, publicApp)}/auth/google/redirect-callback";

    return Results.Ok(new GoogleRedirectStartResponse(nonce, state, loginUri, challengeTtlSeconds));
}).RequireRateLimiting("public-auth");

auth.MapPost("/google/callback", async (
    GoogleCallbackRequest request,
    HttpContext http,
    IGoogleIdTokenValidator google,
    IUsersRepository users,
    IDevicesRepository devices,
    IJwtIssuer jwt,
    IRefreshTokenProtector refreshProtector,
    RefreshTokenOptions refreshOptions,
    TimeProvider clock,
    IdentityMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var loginStarted = Stopwatch.GetTimestamp();

    if (string.IsNullOrWhiteSpace(request.IdToken)
        || string.IsNullOrWhiteSpace(request.Device.InstallId)
        || string.IsNullOrWhiteSpace(request.Device.Platform))
    {
        RecordLoginFailure(metrics, loginStarted, "invalid_request");
        return ProblemHttp.InvalidRequest();
    }

    var challengeToken = !string.IsNullOrWhiteSpace(request.ChallengeToken)
        ? request.ChallengeToken!
        : http.Request.Cookies.TryGetValue(HttpHelpers.ChallengeCookieName, out var cookieToken)
            ? cookieToken
            : null;

    if (string.IsNullOrWhiteSpace(challengeToken)
        || !http.Request.Headers.TryGetValue("x-csrf-token", out var csrfHeader)
        || !HttpHelpers.TryReadChallengeCookie(
            challengeToken,
            csrfHeader.ToString(),
            refreshOptions.Pepper,
            clock.GetUtcNow(),
             out var expectedNonce))
    {
        RecordLoginFailure(metrics, loginStarted, "invalid_challenge");
        return ProblemHttp.InvalidRequest("Invalid or expired login challenge.");
    }

    http.Response.Cookies.Delete(HttpHelpers.ChallengeCookieName, new CookieOptions { Path = "/auth" });

    GooglePrincipal googleUser;
    try
    {
        googleUser = await google.ValidateAsync(request.IdToken, expectedNonce, cancellationToken);
    }
    catch (IdentityDependencyException) when (!cancellationToken.IsCancellationRequested)
    {
        RecordLoginFailure(metrics, loginStarted, "google_dependency_unavailable");
        return ProblemHttp.DependencyUnavailable();
    }
    catch (Exception) when (!cancellationToken.IsCancellationRequested)
    {
        RecordLoginFailure(metrics, loginStarted, "invalid_google_token");
        return ProblemHttp.InvalidGoogleToken();
    }

    var (authResponse, error) = await IssueAuthSessionAsync(
        googleUser,
        request.Device,
        request.RememberMe,
        http,
        users,
        devices,
        jwt,
        refreshProtector,
        refreshOptions,
        clock,
        metrics,
        loginStarted,
        cancellationToken);
    return error ?? Results.Ok(authResponse);
}).RequireRateLimiting("public-auth");

auth.MapPost("/google/redirect-callback", async (
    HttpContext http,
    IGoogleIdTokenValidator google,
    IUsersRepository users,
    IDevicesRepository devices,
    IJwtIssuer jwt,
    IRefreshTokenProtector refreshProtector,
    RefreshTokenOptions refreshOptions,
    TimeProvider clock,
    IdentityMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var loginStarted = Stopwatch.GetTimestamp();

    if (!http.Request.HasFormContentType)
    {
        RecordLoginFailure(metrics, loginStarted, "invalid_request");
        return ProblemHttp.InvalidRequest();
    }

    var form = await http.Request.ReadFormAsync(cancellationToken);
    var credential = form["credential"].ToString();
    var stateValue = form["state"].ToString();
    if (string.IsNullOrWhiteSpace(credential)
        || string.IsNullOrWhiteSpace(stateValue)
        || !HttpHelpers.TryReadRedirectState(
            stateValue,
            refreshOptions.Pepper,
            clock.GetUtcNow(),
            out var redirectState)
        || !IsSafeLocalReturnPath(redirectState.ReturnPath))
    {
        RecordLoginFailure(metrics, loginStarted, "invalid_redirect_state");
        return ProblemHttp.InvalidRequest("Invalid or expired login redirect state.");
    }

    GooglePrincipal googleUser;
    try
    {
        googleUser = await google.ValidateAsync(credential, redirectState.Nonce, cancellationToken);
    }
    catch (IdentityDependencyException) when (!cancellationToken.IsCancellationRequested)
    {
        RecordLoginFailure(metrics, loginStarted, "google_dependency_unavailable");
        return ProblemHttp.DependencyUnavailable();
    }
    catch (Exception) when (!cancellationToken.IsCancellationRequested)
    {
        RecordLoginFailure(metrics, loginStarted, "invalid_google_token");
        return ProblemHttp.InvalidGoogleToken();
    }

    var (authResponse, error) = await IssueAuthSessionAsync(
        googleUser,
        redirectState.Device,
        redirectState.RememberMe,
        http,
        users,
        devices,
        jwt,
        refreshProtector,
        refreshOptions,
        clock,
        metrics,
        loginStarted,
        cancellationToken);
    if (error is not null || authResponse is null)
    {
        return error ?? ProblemHttp.InvalidRequest();
    }

    return SeeOther(http.Response, BuildRedirectHandoff(
        redirectState.ReturnPath,
        authResponse.CsrfToken,
        redirectState.RememberMe));
}).RequireRateLimiting("public-auth");

auth.MapPost("/refresh", async (
    HttpContext http,
    IUsersRepository users,
    IDevicesRepository devices,
    IJwtIssuer jwt,
    IRefreshTokenProtector refreshProtector,
    RefreshTokenOptions refreshOptions,
    TimeProvider clock,
    IdentityMetrics metrics,
    CancellationToken cancellationToken) =>
{
    if (!ValidateDoubleSubmitCsrf(http))
    {
        metrics.RefreshFailure("csrf");
        return ProblemHttp.InvalidRefreshToken();
    }

    if (!http.Request.Cookies.TryGetValue(refreshOptions.CookieName, out var refreshToken))
    {
        metrics.RefreshFailure("missing_cookie");
        return ProblemHttp.InvalidRefreshToken();
    }

    var parsed = refreshProtector.Parse(refreshToken);
    if (parsed is null)
    {
        metrics.RefreshFailure("parse");
        return ProblemHttp.InvalidRefreshToken();
    }

    var user = await users.GetByIdAsync(parsed.UserId, cancellationToken);
    if (user is null)
    {
        metrics.RefreshFailure("missing_user");
        return ProblemHttp.InvalidRefreshToken();
    }

    if (user.DeletedAt is not null)
    {
        metrics.RefreshFailure("user_deleted");
        return ProblemHttp.UserDeleted();
    }

    var device = await devices.GetAsync(parsed.UserId, parsed.DeviceId, cancellationToken);
    if (device is null)
    {
        metrics.RefreshFailure("missing_device");
        return ProblemHttp.InvalidRefreshToken();
    }

    if (device.RevokedAt is not null)
    {
        metrics.RefreshFailure("device_revoked");
        return ProblemHttp.DeviceRevoked();
    }

    var now = clock.GetUtcNow();
    if (!IdentityRules.IsRefreshSessionActive(device, now)
        || device.Session.SessionId != parsed.SessionId
        || string.IsNullOrWhiteSpace(device.Session.RefreshTokenHash)
        || !refreshProtector.FixedTimeEquals(refreshToken, device.Session.RefreshTokenHash))
    {
        metrics.RefreshFailure("invalid_hash");
        return ProblemHttp.InvalidRefreshToken();
    }

    var issuedRefresh = refreshProtector.Issue(user.Id, device.Id, parsed.SessionId);
    device.LastSeenAt = now;
    device.Ttl = IdentityRules.DeviceTtlSeconds;
    device.Session.RefreshTokenHash = issuedRefresh.Hash;
    device.Session.RefreshTokenRotatedAt = now;
    device.Session.RefreshTokenExpiresAt = now.AddDays(refreshOptions.LifetimeDays);
    device.Session.LastIp = ClientIp(http);
    await devices.UpdateAsync(device, cancellationToken);

    var accessToken = jwt.Issue(user.Id, device.Id, user.Handle);
    var csrf = SetAuthCookies(http.Response, refreshOptions, clock, issuedRefresh.Token, device.Session.RememberMe);
    metrics.RefreshSuccess();
    return Results.Ok(new AuthTokenResponse(
        accessToken.AccessToken,
        "Bearer",
        accessToken.ExpiresIn,
        csrf,
        user.ToMe(),
        device.Id));
}).RequireRateLimiting("public-auth");

auth.MapPost("/logout", async (
    ClaimsPrincipal principal,
    HttpContext http,
    IUsersRepository users,
    IDevicesRepository devices,
    RefreshTokenOptions refresh,
    TimeProvider clock,
    IdentityMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = await ResolveCurrentAsync(principal, users, devices, cancellationToken);
    if (current.Error is not null)
    {
        return current.Error;
    }

    IdentityRules.RevokeSession(current.Device!, clock.GetUtcNow());
    await devices.UpdateAsync(current.Device!, cancellationToken);
    ClearAuthCookies(http.Response, refresh);
    metrics.LogoutSuccess();
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/users/me", async (
    ClaimsPrincipal principal,
    IUsersRepository users,
    IDevicesRepository devices,
    CancellationToken cancellationToken) =>
{
    var current = await ResolveCurrentAsync(principal, users, devices, cancellationToken);
    return current.Error ?? Results.Ok(current.User!.ToMe());
}).RequireAuthorization().WithTags("Users");

app.MapPatch("/users/me", async (
    PatchUserMeRequest request,
    HttpRequest httpRequest,
    ClaimsPrincipal principal,
    IUsersRepository users,
    IDevicesRepository devices,
    TimeProvider clock,
    CancellationToken cancellationToken) =>
{
    var current = await ResolveCurrentAsync(principal, users, devices, cancellationToken);
    if (current.Error is not null)
    {
        return current.Error;
    }

    var errors = ProfilePatchRules.Validate(request.Name, request.Handle, request.Phone);
    if (errors.Count > 0)
    {
        return ProblemHttp.InvalidRequest("Invalid profile patch.");
    }

    ProfilePatchRules.Apply(
        current.User!,
        request.Name,
        request.Handle,
        request.Phone,
        request.Preferences,
        clock.GetUtcNow());

    try
    {
        var ifMatch = httpRequest.Headers.TryGetValue("If-Match", out var header) ? header.ToString() : null;
        await users.UpdateAsync(current.User!, ifMatch, cancellationToken);
    }
    catch (IdentityConflictException ex) when (ex.Code == "handle_already_taken")
    {
        return ProblemHttp.HandleTaken();
    }

    return Results.Ok(current.User!.ToMe());
}).RequireAuthorization().WithTags("Users");

app.MapPost("/users/me/avatar", async (
    HttpRequest request,
    ClaimsPrincipal principal,
    IUsersRepository users,
    IDevicesRepository devices,
    IAvatarStorage avatarStorage,
    TimeProvider clock,
    IdentityMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = await ResolveCurrentAsync(principal, users, devices, cancellationToken);
    if (current.Error is not null)
    {
        return current.Error;
    }

    if (!request.HasFormContentType)
    {
        return ProblemHttp.InvalidRequest("Expected multipart/form-data.");
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files["avatar"] ?? form.Files.FirstOrDefault();
    if (file is null)
    {
        return ProblemHttp.InvalidRequest("Missing avatar file.");
    }

    if (file.Length > 2 * 1024 * 1024)
    {
        return ProblemHttp.AvatarTooLarge();
    }

    if (file.ContentType is not ("image/jpeg" or "image/png" or "image/webp"))
    {
        return ProblemHttp.UnsupportedAvatarType();
    }

    AvatarUploadResult upload;
    await using (var stream = file.OpenReadStream())
    {
        upload = await avatarStorage.UploadAsync(current.User!.Id, stream, file.ContentType, cancellationToken);
    }

    var oldAvatarUrl = current.User!.AvatarUrl;
    current.User.AvatarUrl = upload.Url;
    current.User.UpdatedAt = clock.GetUtcNow();

    try
    {
        await users.UpdateAsync(current.User, null, cancellationToken);
    }
    catch
    {
        await avatarStorage.DeleteByUrlAsync(upload.Url, CancellationToken.None);
        throw;
    }

    try
    {
        await avatarStorage.DeleteByUrlAsync(oldAvatarUrl, CancellationToken.None);
    }
    catch
    {
    }

    metrics.AvatarUploaded();
    return Results.Ok(new AvatarResponse(upload.Url));
}).RequireAuthorization().WithTags("Users");

app.MapDelete("/users/me", async (
    ClaimsPrincipal principal,
    HttpContext http,
    IUsersRepository users,
    IDevicesRepository devices,
    RefreshTokenOptions refresh,
    TimeProvider clock,
    IdentityMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = await ResolveCurrentAsync(principal, users, devices, cancellationToken);
    if (current.Error is not null)
    {
        return current.Error;
    }

    var now = clock.GetUtcNow();
    IdentityRules.ApplySoftDelete(current.User!, now);
    await users.UpdateAsync(current.User!, null, cancellationToken);

    var userDevices = await devices.ListByUserAsync(current.User!.Id, cancellationToken);
    foreach (var device in userDevices)
    {
        IdentityRules.RevokeSession(device, now);
        await devices.UpdateAsync(device, cancellationToken);
    }

    ClearAuthCookies(http.Response, refresh);
    metrics.UserDeleted();
    return Results.NoContent();
}).RequireAuthorization().WithTags("Users");

app.MapGet("/.well-known/openid-configuration", (HttpRequest request, JwtOptions jwt) =>
{
    var baseUrl = $"{request.Scheme}://{request.Host}";
    return Results.Ok(new OpenIdConfigurationResponse(
        jwt.Issuer,
        $"{baseUrl}/.well-known/jwks.json",
        ["RS256"]));
}).WithTags("Discovery");

app.MapGet("/.well-known/jwks.json", (IJwtIssuer issuer) => Results.Ok(issuer.GetJwks()))
    .WithTags("Discovery");

app.Run();

T Bind<T>(string sectionName) where T : new() =>
    builder.Configuration.GetSection(sectionName).Get<T>() ?? new T();

static DeviceProfile ToDeviceProfile(DeviceRequest request) =>
    new(
        request.InstallId,
        request.Platform.Trim().ToLowerInvariant(),
        request.UserAgent,
        request.Model,
        request.OsVersion,
        request.AppVersion);

static void RecordLoginSuccess(IdentityMetrics metrics, long started)
{
    metrics.LoginSuccess();
    metrics.LoginDuration(Stopwatch.GetElapsedTime(started), "success", null);
}

static void RecordLoginFailure(IdentityMetrics metrics, long started, string reason)
{
    metrics.LoginFailure(reason);
    metrics.LoginDuration(Stopwatch.GetElapsedTime(started), "failure", reason);
}

static async Task<(AuthTokenResponse? AuthResponse, IResult? Error)> IssueAuthSessionAsync(
    GooglePrincipal googleUser,
    DeviceRequest deviceRequest,
    bool rememberMe,
    HttpContext http,
    IUsersRepository users,
    IDevicesRepository devices,
    IJwtIssuer jwt,
    IRefreshTokenProtector refreshProtector,
    RefreshTokenOptions refreshOptions,
    TimeProvider clock,
    IdentityMetrics metrics,
    long loginStarted,
    CancellationToken cancellationToken)
{
    try
    {
        var now = clock.GetUtcNow();
        var user = await users.GetByGoogleSubAsync(googleUser.Subject, cancellationToken);
        if (user is { DeletedAt: not null })
        {
            RecordLoginFailure(metrics, loginStarted, "user_deleted");
            return (null, ProblemHttp.UserDeleted());
        }

        if (user is null)
        {
            user = await CreateUserAsync(googleUser, users, now, cancellationToken);
            if (user is null)
            {
                RecordLoginFailure(metrics, loginStarted, "email_already_registered");
                return (null, ProblemHttp.EmailRegistered());
            }
        }
        else
        {
            user.Auth.Google.Email = googleUser.Email;
            user.Auth.Google.EmailVerified = googleUser.EmailVerified;
            user.Auth.Google.PictureUrl = googleUser.PictureUrl;
            user.Auth.Google.HostedDomain = googleUser.HostedDomain;
            user.Auth.LastLoginAt = now;
            user.UpdatedAt = now;
            await users.UpdateLoginSnapshotAsync(
                user.Id,
                googleUser.Email,
                googleUser.EmailVerified,
                googleUser.PictureUrl,
                googleUser.HostedDomain,
                now,
                cancellationToken);
        }

        var profile = ToDeviceProfile(deviceRequest);
        var deviceId = IdentityRules.DeviceId(user.Id, profile.Platform, profile.InstallId);
        var sessionId = IdentityRules.NewSessionId();
        var issuedRefresh = refreshProtector.Issue(user.Id, deviceId, sessionId);
        var existingDevice = await devices.GetAsync(user.Id, deviceId, cancellationToken);
        var device = IdentityRules.UpsertDeviceSession(
            existingDevice,
            user.Id,
            deviceId,
            profile,
            sessionId,
            rememberMe,
            issuedRefresh.Hash,
            ClientIp(http),
            refreshOptions.LifetimeDays,
            now);
        await devices.UpsertAsync(device, cancellationToken);

        var accessToken = jwt.Issue(user.Id, deviceId, user.Handle);
        var csrf = SetAuthCookies(http.Response, refreshOptions, clock, issuedRefresh.Token, rememberMe);
        RecordLoginSuccess(metrics, loginStarted);
        return (new AuthTokenResponse(
            accessToken.AccessToken,
            "Bearer",
            accessToken.ExpiresIn,
            csrf,
            user.ToMe(),
            deviceId), null);
    }
    catch (CosmosException) when (!cancellationToken.IsCancellationRequested)
    {
        RecordLoginFailure(metrics, loginStarted, "dependency_unavailable");
        return (null, ProblemHttp.DependencyUnavailable());
    }
    catch (IdentityDependencyException) when (!cancellationToken.IsCancellationRequested)
    {
        RecordLoginFailure(metrics, loginStarted, "dependency_unavailable");
        return (null, ProblemHttp.DependencyUnavailable());
    }
}

static string SetAuthCookies(
    HttpResponse response,
    RefreshTokenOptions options,
    TimeProvider clock,
    string refreshToken,
    bool rememberMe)
{
    var now = clock.GetUtcNow();
    response.Cookies.Append(options.CookieName, refreshToken, HttpHelpers.RefreshCookieOptions(options, rememberMe, now));
    var csrf = HttpHelpers.NewSecret();
    response.Cookies.Append(HttpHelpers.CsrfCookieName, csrf, HttpHelpers.CsrfCookieOptions(options, now));
    return csrf;
}

static void ClearAuthCookies(HttpResponse response, RefreshTokenOptions options)
{
    response.Cookies.Delete(options.CookieName, HttpHelpers.ExpiredCookieOptions(options, httpOnly: true));
    response.Cookies.Delete(HttpHelpers.CsrfCookieName, HttpHelpers.ExpiredCookieOptions(options, httpOnly: false));
}

static bool ValidateDoubleSubmitCsrf(HttpContext http)
{
    return http.Request.Cookies.TryGetValue(HttpHelpers.CsrfCookieName, out var cookie)
        && http.Request.Headers.TryGetValue("x-csrf-token", out var header)
        && HttpHelpers.FixedTimeEquals(cookie, header.ToString());
}

static string ClientIp(HttpContext http) =>
    http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static string PublicBaseUrl(HttpRequest request, PublicAppOptions? publicApp = null)
{
    if (!string.IsNullOrWhiteSpace(publicApp?.BaseUrl))
    {
        return publicApp.BaseUrl.TrimEnd('/');
    }

    var proto = request.Headers.TryGetValue("X-Forwarded-Proto", out var forwardedProto)
        ? forwardedProto.ToString().Split(',', 2)[0].Trim()
        : request.Scheme;
    var host = request.Headers.TryGetValue("X-Forwarded-Host", out var forwardedHost)
        ? forwardedHost.ToString().Split(',', 2)[0].Trim()
        : request.Host.Value;

    return $"{proto}://{host}".TrimEnd('/');
}

static string NormalizeReturnPath(string? returnPath) =>
    IsSafeLocalReturnPath(returnPath) ? returnPath! : "/";

static bool IsSafeLocalReturnPath(string? returnPath)
{
    if (string.IsNullOrWhiteSpace(returnPath)
        || !returnPath.StartsWith("/", StringComparison.Ordinal)
        || returnPath.StartsWith("//", StringComparison.Ordinal)
        || returnPath.Contains("\\", StringComparison.Ordinal)
        || returnPath.Contains("#", StringComparison.Ordinal))
    {
        return false;
    }

    return Uri.TryCreate(returnPath, UriKind.Relative, out _);
}

static IResult SeeOther(HttpResponse response, string location)
{
    response.Headers.Location = location;
    return Results.StatusCode(StatusCodes.Status303SeeOther);
}

static string BuildRedirectHandoff(string returnPath, string csrf, bool rememberMe)
{
    var fragment =
        $"auth_redirect=1&csrf={Uri.EscapeDataString(csrf)}&remember={(rememberMe ? "1" : "0")}";
    return $"{returnPath}#{fragment}";
}

static async Task<UserDocument?> CreateUserAsync(
    GooglePrincipal googleUser,
    IUsersRepository users,
    DateTimeOffset now,
    CancellationToken cancellationToken)
{
    var userId = IdentityRules.NewUserId();
    var displayName = string.IsNullOrWhiteSpace(googleUser.Name)
        ? googleUser.Email.Split('@', 2)[0]
        : googleUser.Name;

    foreach (var handle in HandleRules.Candidates(displayName, googleUser.Email).Take(20))
    {
        var user = IdentityRules.CreateUser(
            userId,
            handle,
            displayName,
            googleUser.Email,
            googleUser.Subject,
            googleUser.PictureUrl,
            googleUser.HostedDomain,
            now);
        try
        {
            await users.CreateAsync(user, cancellationToken);
            return user;
        }
        catch (IdentityConflictException ex) when (ex.Code == "unique_key_conflict")
        {
            continue;
        }
    }

    return null;
}

static async Task<(UserDocument? User, DeviceDocument? Device, IResult? Error)> ResolveCurrentAsync(
    ClaimsPrincipal principal,
    IUsersRepository users,
    IDevicesRepository devices,
    CancellationToken cancellationToken)
{
    var identity = principal.ToAuthenticatedIdentity();
    if (identity is null)
    {
        return (null, null, Results.Unauthorized());
    }

    var user = await users.GetByIdAsync(identity.UserId, cancellationToken);
    if (user is null)
    {
        return (null, null, Results.Unauthorized());
    }

    if (user.DeletedAt is not null)
    {
        return (user, null, ProblemHttp.UserDeleted());
    }

    var device = await devices.GetAsync(identity.UserId, identity.DeviceId, cancellationToken);
    if (device is { RevokedAt: not null })
    {
        return (user, device, ProblemHttp.DeviceRevoked());
    }

    return (user, device, null);
}

static void NormalizeSecrets(
    IWebHostEnvironment environment,
    JwtOptions jwt,
    RefreshTokenOptions refresh)
{
    if (IsPlaceholder(jwt.PrivateKeyPem))
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("Jwt:PrivateKeyPem must come from secrets/env vars.");
        }

        jwt.PrivateKeyPem = DevelopmentPem.Value;
    }

    if (IsPlaceholder(refresh.Pepper))
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("RefreshTokens:Pepper must come from secrets/env vars.");
        }

        refresh.Pepper = "local-development-refresh-token-pepper";
    }
}

static bool IsPlaceholder(string? value) =>
    string.IsNullOrWhiteSpace(value) || value.StartsWith("<", StringComparison.Ordinal);

static class DevelopmentPem
{
    public static readonly string Value = Create();

    private static string Create()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }
}

public partial class Program;
