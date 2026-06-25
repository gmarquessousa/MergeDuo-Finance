using System.Security.Claims;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using MergeDuo.Profile.Api;
using MergeDuo.Profile.Api.Security;
using MergeDuo.Profile.Domain.Abstractions;
using MergeDuo.Profile.Domain.Documents;
using MergeDuo.Profile.Domain.Exceptions;
using MergeDuo.Profile.Domain.Options;
using MergeDuo.Profile.Domain.Rules;
using MergeDuo.Profile.Domain.Services;
using MergeDuo.Profile.Infra.Cosmos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Azure.Cosmos;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

var cosmosOptions = Bind<CosmosOptions>("Cosmos");
var jwtOptions = Bind<JwtOptions>("Jwt");
var corsOptions = Bind<CorsOptions>("Cors");
var statsOptions = Bind<StatsOptions>("Stats");

builder.Services.AddSingleton(cosmosOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(corsOptions);
builder.Services.AddSingleton(statsOptions);

builder.Services.AddSingleton<ProfileMetrics>();
builder.Services.AddSingleton<ICosmosDiagnosticsRecorder>(sp => sp.GetRequiredService<ProfileMetrics>());

builder.Services.AddSingleton<CosmosClient>(sp => CosmosClientFactory.Create(sp.GetRequiredService<CosmosOptions>()));
builder.Services.AddSingleton<IUsersRepository, UsersRepository>();
builder.Services.AddSingleton<IPartnershipsRepository, PartnershipsRepository>();
builder.Services.AddSingleton<ITransactionsStatsRepository, TransactionsStatsRepository>();
builder.Services.AddSingleton<IProfileReadinessProbe, CosmosReadinessProbe>();

builder.Services.AddSingleton<IProfileQueryService, ProfileQueryService>();
builder.Services.AddSingleton<IProfileStatsService, ProfileStatsService>();

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
    options.OnRejected = async (context, cancellationToken) =>
        await ProblemHttp.WriteAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "rate_limited",
            "Rate limit exceeded.",
            cancellationToken);

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

    options.AddPolicy("profile-read", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("handle-lookup", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("stats", context =>
    {
        var isFresh = context.Request.Query.TryGetValue("fresh", out var value)
            && bool.TryParse(value, out var parsed)
            && parsed;

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{RateLimitKey(context)}:{(isFresh ? "fresh" : "cache")}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = isFresh ? 6 : 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

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
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "userId"
        };

        var staticKey = JwtKeyFactory.TryCreateStaticKey(jwtOptions);
        if (staticKey is not null)
        {
            options.TokenValidationParameters.IssuerSigningKey = staticKey;
        }
        else
        {
            var provider = new JwksSigningKeyProvider(new HttpClient(), jwtOptions, TimeProvider.System);
            options.TokenValidationParameters.IssuerSigningKeyResolver = provider.ResolveSigningKeys;
        }

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var detail = builder.Environment.IsEnvironment("Testing") && context.AuthenticateFailure is not null
                    ? context.AuthenticateFailure.Message
                    : "JWT is missing, invalid or expired.";
                await ProblemHttp.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "unauthorized",
                    detail,
                    context.HttpContext.RequestAborted);
            },
            OnForbidden = async context =>
                await ProblemHttp.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "forbidden",
                    "Forbidden.",
                    context.HttpContext.RequestAborted)
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
        .WithMetrics(metrics => metrics.AddMeter(ProfileMetrics.MeterName));
}

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, code, detail) = exception switch
        {
            ProfileConflictException ex => (StatusCodes.Status409Conflict, ex.Code, ex.Message),
            ProfileNotFoundException ex => (StatusCodes.Status404NotFound, ex.Code, ex.Message),
            ProfileAccessException ex => (StatusCodes.Status403Forbidden, ex.Code, ex.Message),
            ProfileDependencyException => (StatusCodes.Status503ServiceUnavailable, "profile_dependency_unavailable", "Profile dependency unavailable."),
            ArgumentException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            _ => (StatusCodes.Status500InternalServerError, "profile_unhandled_error", "Unhandled profile error.")
        };

        await ProblemHttp.WriteAsync(context, status, code, detail, context.RequestAborted);
    });
});

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/healthz");
app.MapGet("/readyz", async (
    IProfileReadinessProbe readiness,
    CancellationToken cancellationToken) =>
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

    try
    {
        return await readiness.IsReadyAsync(linked.Token)
            ? Results.Ok(new { status = "ready" })
            : ProblemHttp.DependencyUnavailable();
    }
    catch
    {
        return ProblemHttp.DependencyUnavailable();
    }
});

var users = app.MapGroup("/users").RequireAuthorization();

users.MapGet("/by-handle/{handle}", async (
    string handle,
    ClaimsPrincipal principal,
    IUsersRepository usersRepository,
    IProfileQueryService profiles,
    ProfileMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var normalized = HandleRules.Normalize(handle);
    if (!HandleRules.IsValid(normalized))
    {
        metrics.HandleLookupRequested(StatusCodes.Status400BadRequest, "invalid_handle");
        return ProblemHttp.InvalidHandle();
    }

    var current = await ResolveCurrentAsync(principal, usersRepository, cancellationToken);
    if (current.Error is not null)
    {
        metrics.HandleLookupRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await profiles.GetProfileByHandleAsync(current.User!, normalized, cancellationToken);
    if (response is null)
    {
        metrics.HandleLookupRequested(StatusCodes.Status404NotFound, "not_found");
        return ProblemHttp.ProfileNotFound();
    }

    metrics.HandleLookupRequested(StatusCodes.Status200OK, response.Relationship is null ? "public" : "relationship");
    if (response.Relationship is not null)
    {
        metrics.RelationshipFound();
    }

    return Results.Ok(response);
}).RequireRateLimiting("handle-lookup");

users.MapGet("/{userId:regex(^usr_[A-Za-z0-9_-]+$)}", async (
    string userId,
    ClaimsPrincipal principal,
    IUsersRepository usersRepository,
    IProfileQueryService profiles,
    ProfileMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = await ResolveCurrentAsync(principal, usersRepository, cancellationToken);
    if (current.Error is not null)
    {
        metrics.PublicProfileRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await profiles.GetProfileByIdAsync(current.User!, userId, cancellationToken);
    if (response is null)
    {
        metrics.PublicProfileRequested(StatusCodes.Status404NotFound, "not_found");
        return ProblemHttp.ProfileNotFound();
    }

    metrics.PublicProfileRequested(StatusCodes.Status200OK, response.Relationship is null ? "public" : "relationship");
    if (response.Relationship is not null)
    {
        metrics.RelationshipFound();
    }

    return Results.Ok(response);
}).RequireRateLimiting("profile-read");

users.MapGet("/{userId}", (string userId, ProfileMetrics metrics) =>
{
    metrics.PublicProfileRequested(StatusCodes.Status400BadRequest, "invalid_user_id");
    return ProblemHttp.InvalidUserId();
}).RequireRateLimiting("profile-read");

var me = app.MapGroup("/me").RequireAuthorization();
me.MapGet("/stats", async (
    bool? fresh,
    ClaimsPrincipal principal,
    IUsersRepository usersRepository,
    IProfileStatsService stats,
    ProfileMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var recompute = fresh == true;
    var current = await ResolveCurrentAsync(principal, usersRepository, cancellationToken);
    if (current.Error is not null)
    {
        return current.Error;
    }

    try
    {
        var response = await stats.GetCurrentStatsAsync(current.User!, recompute, cancellationToken);
        metrics.StatsResult(response.Source, response.IsStale);
        return Results.Ok(response);
    }
    catch
    {
        if (recompute)
        {
            metrics.StatsRecomputeFailed();
        }

        throw;
    }
}).RequireRateLimiting("stats");

app.Run();

T Bind<T>(string sectionName) where T : new() =>
    builder.Configuration.GetSection(sectionName).Get<T>() ?? new T();

static string RateLimitKey(HttpContext context) =>
    CurrentUserId(context.User)
    ?? context.Connection.RemoteIpAddress?.ToString()
    ?? "unknown";

static async Task<(UserDocument? User, IResult? Error, int StatusCode)> ResolveCurrentAsync(
    ClaimsPrincipal principal,
    IUsersRepository users,
    CancellationToken cancellationToken)
{
    var userId = CurrentUserId(principal);
    if (string.IsNullOrWhiteSpace(userId) || !UserIdRules.IsValid(userId))
    {
        return (null, ProblemHttp.Unauthorized(), StatusCodes.Status401Unauthorized);
    }

    var user = await users.GetByIdAsync(userId, cancellationToken);
    if (user is null)
    {
        return (null, ProblemHttp.Unauthorized(), StatusCodes.Status401Unauthorized);
    }

    if (user.DeletedAt is not null)
    {
        return (user, ProblemHttp.UserDeleted(), StatusCodes.Status403Forbidden);
    }

    return (user, null, StatusCodes.Status200OK);
}

static string? CurrentUserId(ClaimsPrincipal principal) =>
    principal.FindFirst("userId")?.Value
    ?? principal.FindFirst("sub")?.Value
    ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

public partial class Program;
