using System.Security.Claims;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using MergeDuo.Partnership.Api;
using MergeDuo.Partnership.Api.Security;
using MergeDuo.Partnership.Domain.Abstractions;
using MergeDuo.Partnership.Domain.Contracts;
using MergeDuo.Partnership.Domain.Documents;
using MergeDuo.Partnership.Domain.Exceptions;
using MergeDuo.Partnership.Domain.Options;
using MergeDuo.Partnership.Domain.Rules;
using MergeDuo.Partnership.Domain.Services;
using MergeDuo.Partnership.Infra.Cosmos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Azure.Cosmos;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

var cosmosOptions = Bind<CosmosOptions>("Cosmos");
var jwtOptions = Bind<JwtOptions>("Jwt");
var corsOptions = Bind<CorsOptions>("Cors");
var publicAppOptions = Bind<PublicAppOptions>("PublicApp");
var inviteOptions = Bind<InviteOptions>("Invite");
var rateLimitOptions = Bind<RateLimitOptions>("RateLimit");

builder.Services.AddSingleton(cosmosOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(corsOptions);
builder.Services.AddSingleton(publicAppOptions);
builder.Services.AddSingleton(inviteOptions);
builder.Services.AddSingleton(rateLimitOptions);

builder.Services.AddSingleton<PartnershipMetrics>();
builder.Services.AddSingleton<ICosmosDiagnosticsRecorder>(sp => sp.GetRequiredService<PartnershipMetrics>());

builder.Services.AddSingleton<CosmosClient>(sp => CosmosClientFactory.Create(sp.GetRequiredService<CosmosOptions>()));
builder.Services.AddSingleton<IUsersReadRepository, UsersReadRepository>();
builder.Services.AddSingleton<IInvitesRepository, InvitesRepository>();
builder.Services.AddSingleton<IPartnershipsRepository, PartnershipsRepository>();
builder.Services.AddSingleton<IPartnershipReadinessProbe, CosmosReadinessProbe>();
builder.Services.AddSingleton<IPartnershipWorkflow, PartnershipWorkflow>();

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
                PermitLimit = rateLimitOptions.GlobalPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("invite-create", context =>
        FixedWindow(RateLimitKey(context), rateLimitOptions.InviteCreatePermitLimit));

    options.AddPolicy("invite-preview", context =>
        FixedWindow(InvitePreviewRateLimitKey(context), rateLimitOptions.InvitePreviewPermitLimit));

    options.AddPolicy("invite-accept", context =>
        FixedWindow(RateLimitKey(context), rateLimitOptions.InviteAcceptPermitLimit));

    options.AddPolicy("partnership", context =>
        FixedWindow(RateLimitKey(context), rateLimitOptions.PartnershipPermitLimit));
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
        .WithMetrics(metrics => metrics.AddMeter(PartnershipMetrics.MeterName));
}

var app = builder.Build();

if (cosmosOptions.AutoCreateContainers)
{
    await CosmosContainerInitializer.EnsureCreatedAsync(
        app.Services.GetRequiredService<CosmosClient>(),
        cosmosOptions,
        app.Lifetime.ApplicationStopping);
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, code, detail) = exception switch
        {
            PartnershipBadRequestException ex => (StatusCodes.Status400BadRequest, ex.Code, ex.Message),
            PartnershipAccessException ex => (StatusCodes.Status403Forbidden, ex.Code, ex.Message),
            PartnershipNotFoundException ex => (StatusCodes.Status404NotFound, ex.Code, ex.Message),
            PartnershipConflictException ex => (StatusCodes.Status409Conflict, ex.Code, ex.Message),
            PartnershipGoneException ex => (StatusCodes.Status410Gone, ex.Code, ex.Message),
            PartnershipThrottledException ex => (StatusCodes.Status429TooManyRequests, ex.Code, ex.Message),
            PartnershipDependencyException ex => (StatusCodes.Status503ServiceUnavailable, ex.Code, ex.Message),
            ArgumentException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            _ => (StatusCodes.Status500InternalServerError, "partnership_unhandled_error", "Unhandled partnership error.")
        };

        if (exception is PartnershipThrottledException throttled && throttled.RetryAfter is { } retryAfter)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.Response.Headers["Retry-After"] = seconds.ToString();
        }

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
    IPartnershipReadinessProbe readiness,
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

var invites = app.MapGroup("/invites");

invites.MapPost("", async (
    CreateInviteRequest request,
    ClaimsPrincipal principal,
    IUsersReadRepository users,
    IPartnershipWorkflow workflow,
    PartnershipMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = await ResolveCurrentAsync(principal, users, cancellationToken);
    if (current.Error is not null)
    {
        metrics.InviteCreated(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await workflow.CreateInviteAsync(current.User!, request, cancellationToken);
    metrics.InviteCreated(StatusCodes.Status200OK, response.Status == InviteStatuses.Pending ? "created_or_reused" : response.Status);
    return Results.Ok(response);
}).RequireAuthorization().RequireRateLimiting("invite-create");

invites.MapGet("/{token}", async (
    string token,
    IPartnershipWorkflow workflow,
    PartnershipMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var response = await workflow.PreviewInviteAsync(token, cancellationToken);
    metrics.InvitePreviewed(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("invite-preview");

invites.MapPost("/{token}/accept", async (
    string token,
    ClaimsPrincipal principal,
    IUsersReadRepository users,
    IPartnershipWorkflow workflow,
    PartnershipMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = await ResolveCurrentAsync(principal, users, cancellationToken);
    if (current.Error is not null)
    {
        metrics.InviteAccepted(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await workflow.AcceptInviteAsync(current.User!, token, cancellationToken);
    metrics.InviteAccepted(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireAuthorization().RequireRateLimiting("invite-accept");

invites.MapPost("/{token}/revoke", async (
    string token,
    ClaimsPrincipal principal,
    IUsersReadRepository users,
    IPartnershipWorkflow workflow,
    PartnershipMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = await ResolveCurrentAsync(principal, users, cancellationToken);
    if (current.Error is not null)
    {
        metrics.InviteRevoked(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await workflow.RevokeInviteAsync(current.User!.Id, token, cancellationToken);
    metrics.InviteRevoked(StatusCodes.Status200OK, response.Status);
    return Results.Ok(response);
}).RequireAuthorization().RequireRateLimiting("invite-accept");

var partnershipRoutes = app.MapGroup("/partnerships").RequireAuthorization();

partnershipRoutes.MapGet("/me", async (
    ClaimsPrincipal principal,
    IUsersReadRepository users,
    IPartnershipWorkflow workflow,
    PartnershipMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = await ResolveCurrentAsync(principal, users, cancellationToken);
    if (current.Error is not null)
    {
        metrics.CurrentRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await workflow.GetCurrentAsync(current.User!.Id, cancellationToken);
    metrics.CurrentRequested(StatusCodes.Status200OK, response.Partnership is null ? "none" : response.Partnership.Status);
    return Results.Ok(response);
}).RequireRateLimiting("partnership");

partnershipRoutes.MapPost("/{id}/pause", async (
    string id,
    ClaimsPrincipal principal,
    IUsersReadRepository users,
    IPartnershipWorkflow workflow,
    PartnershipMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = await ResolveCurrentAsync(principal, users, cancellationToken);
    if (current.Error is not null)
    {
        metrics.Paused(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await workflow.PauseAsync(current.User!.Id, id, cancellationToken);
    metrics.Paused(StatusCodes.Status200OK, response.Status);
    return Results.Ok(response);
}).RequireRateLimiting("partnership");

partnershipRoutes.MapPost("/{id}/end", async (
    string id,
    ClaimsPrincipal principal,
    IUsersReadRepository users,
    IPartnershipWorkflow workflow,
    PartnershipMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = await ResolveCurrentAsync(principal, users, cancellationToken);
    if (current.Error is not null)
    {
        metrics.Ended(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await workflow.EndAsync(current.User!.Id, id, cancellationToken);
    metrics.Ended(StatusCodes.Status200OK, response.Status);
    return Results.Ok(response);
}).RequireRateLimiting("partnership");

app.Run();

T Bind<T>(string sectionName) where T : new() =>
    builder.Configuration.GetSection(sectionName).Get<T>() ?? new T();

static RateLimitPartition<string> FixedWindow(string key, int permitLimit) =>
    RateLimitPartition.GetFixedWindowLimiter(
        key,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });

static string RateLimitKey(HttpContext context) =>
    CurrentUserId(context.User)
    ?? context.Connection.RemoteIpAddress?.ToString()
    ?? "unknown";

static string InvitePreviewRateLimitKey(HttpContext context)
{
    var token = context.Request.RouteValues.TryGetValue("token", out var routeToken)
        ? routeToken?.ToString()
        : null;
    return !string.IsNullOrWhiteSpace(token)
        ? $"token:{token}"
        : RateLimitKey(context);
}

static async Task<(UserSummaryDocument? User, IResult? Error, int StatusCode)> ResolveCurrentAsync(
    ClaimsPrincipal principal,
    IUsersReadRepository users,
    CancellationToken cancellationToken)
{
    var userId = CurrentUserId(principal);
    if (!UserIdRules.IsValid(userId))
    {
        return (null, ProblemHttp.Unauthorized(), StatusCodes.Status401Unauthorized);
    }

    var user = await users.GetByIdAsync(userId!, cancellationToken);
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
