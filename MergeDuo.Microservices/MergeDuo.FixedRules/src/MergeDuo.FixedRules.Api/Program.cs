using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using MergeDuo.FixedRules.Api;
using MergeDuo.FixedRules.Api.Security;
using MergeDuo.FixedRules.Domain.Abstractions;
using MergeDuo.FixedRules.Domain.Contracts;
using MergeDuo.FixedRules.Domain.Exceptions;
using MergeDuo.FixedRules.Domain.Options;
using MergeDuo.FixedRules.Domain.Rules;
using MergeDuo.FixedRules.Domain.Services;
using MergeDuo.FixedRules.Infra.Cosmos;
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
var previewOptions = Bind<PreviewOptions>("Preview");
var rateLimitOptions = Bind<RateLimitOptions>("RateLimit");

builder.Services.AddSingleton(cosmosOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(corsOptions);
builder.Services.AddSingleton(previewOptions);
builder.Services.AddSingleton(rateLimitOptions);

builder.Services.AddSingleton<FixedRulesMetrics>();
builder.Services.AddSingleton<ICosmosDiagnosticsRecorder>(sp => sp.GetRequiredService<FixedRulesMetrics>());

builder.Services.AddSingleton<CosmosClient>(sp => CosmosClientFactory.Create(sp.GetRequiredService<CosmosOptions>()));
builder.Services.AddSingleton<IFixedRulesRepository, FixedRulesRepository>();
builder.Services.AddSingleton<ICardsReadRepository, CardsReadRepository>();
builder.Services.AddSingleton<IFixedRulesReadinessProbe, CosmosReadinessProbe>();
builder.Services.AddSingleton<IFixedRuleIdGenerator, UlidFixedRuleIdGenerator>();
builder.Services.AddSingleton<IBusinessCalendar, WeekendOnlyBusinessCalendar>();
builder.Services.AddSingleton<IFixedRulePreviewService, FixedRulePreviewService>();
builder.Services.AddSingleton<IFixedRulesService, FixedRulesService>();

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
        FixedWindow(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            rateLimitOptions.GlobalPermitLimit));

    options.AddPolicy("fixed-rules-read", context => FixedWindow(RateLimitKey(context), rateLimitOptions.FixedRuleReadPermitLimit));
    options.AddPolicy("fixed-rules-create", context => FixedWindow(RateLimitKey(context), rateLimitOptions.FixedRuleCreatePermitLimit));
    options.AddPolicy("fixed-rules-patch", context => FixedWindow(RateLimitKey(context), rateLimitOptions.FixedRulePatchPermitLimit));
    options.AddPolicy("fixed-rules-delete", context => FixedWindow(RateLimitKey(context), rateLimitOptions.FixedRuleDeletePermitLimit));
    options.AddPolicy("fixed-rules-preview", context => FixedWindow(RateLimitKey(context), rateLimitOptions.FixedRulePreviewPermitLimit));
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
        .WithMetrics(metrics => metrics.AddMeter(FixedRulesMetrics.MeterName));
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
            FixedRulesBadRequestException ex when ex.Code == "unauthorized" =>
                (StatusCodes.Status401Unauthorized, "unauthorized", ex.Message),
            FixedRulesBadRequestException ex => (StatusCodes.Status400BadRequest, ex.Code, ex.Message),
            FixedRulesNotFoundException ex => (StatusCodes.Status404NotFound, ex.Code, ex.Message),
            FixedRulesConflictException ex => (StatusCodes.Status409Conflict, ex.Code, ex.Message),
            FixedRulesPreconditionFailedException ex => (StatusCodes.Status412PreconditionFailed, ex.Code, ex.Message),
            FixedRulesDependencyException ex => (StatusCodes.Status503ServiceUnavailable, ex.Code, "FixedRules dependency unavailable."),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            ArgumentException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            JsonException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            _ => (StatusCodes.Status500InternalServerError, "fixed_rules_unhandled_error", "Unhandled fixed rules error.")
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
    IFixedRulesReadinessProbe readiness,
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

var fixedRules = app.MapGroup("/fixed-rules").RequireAuthorization();

fixedRules.MapGet("", async (
    string? category,
    string? active,
    bool? includeDeleted,
    ClaimsPrincipal principal,
    IFixedRulesService service,
    FixedRulesMetrics metrics,
    CancellationToken cancellationToken) =>
{
    if (includeDeleted == true)
    {
        metrics.ValidationFailed("include_deleted");
        return ProblemHttp.InvalidRequest("Deleted fixed rules are not available in v1.");
    }

    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.ListRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await service.ListAsync(current.UserId!, category, active, cancellationToken);
    metrics.ListRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("fixed-rules-read");

fixedRules.MapPost("", async (
    CreateFixedRuleRequest? request,
    ClaimsPrincipal principal,
    IFixedRulesService service,
    FixedRulesMetrics metrics,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        metrics.ValidationFailed("invalid_request");
        return ProblemHttp.InvalidRequest("Request body is required.");
    }

    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.Created(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await service.CreateAsync(current.UserId!, request, cancellationToken);
    metrics.Created(StatusCodes.Status201Created, "ok");
    return Results.Created($"/fixed-rules/{response.Id}", response);
}).RequireRateLimiting("fixed-rules-create");

fixedRules.MapGet("{id}", async (
    string id,
    ClaimsPrincipal principal,
    IFixedRulesService service,
    FixedRulesMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.GetRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await service.GetAsync(current.UserId!, id, cancellationToken);
    metrics.GetRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("fixed-rules-read");

fixedRules.MapPatch("{id}", async (
    string id,
    UpdateFixedRuleRequest? request,
    HttpContext httpContext,
    ClaimsPrincipal principal,
    IFixedRulesService service,
    FixedRulesMetrics metrics,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        metrics.ValidationFailed("invalid_request");
        return ProblemHttp.InvalidRequest("Request body is required.");
    }

    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.Updated(current.StatusCode, "auth", hasIfMatch: false);
        return current.Error;
    }

    var ifMatch = httpContext.Request.Headers.IfMatch.ToString();
    var response = await service.PatchAsync(
        current.UserId!,
        id,
        request,
        string.IsNullOrWhiteSpace(ifMatch) ? null : ifMatch,
        cancellationToken);
    metrics.Updated(StatusCodes.Status200OK, "ok", !string.IsNullOrWhiteSpace(ifMatch));
    return Results.Ok(response);
}).RequireRateLimiting("fixed-rules-patch");

fixedRules.MapPost("{id}/pause", async (
    string id,
    HttpContext httpContext,
    ClaimsPrincipal principal,
    IFixedRulesService service,
    FixedRulesMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.Paused(current.StatusCode, "auth", hasIfMatch: false);
        return current.Error;
    }

    var ifMatch = httpContext.Request.Headers.IfMatch.ToString();
    var response = await service.PauseAsync(
        current.UserId!,
        id,
        string.IsNullOrWhiteSpace(ifMatch) ? null : ifMatch,
        cancellationToken);
    metrics.Paused(StatusCodes.Status200OK, "ok", !string.IsNullOrWhiteSpace(ifMatch));
    return Results.Ok(response);
}).RequireRateLimiting("fixed-rules-patch");

fixedRules.MapPost("{id}/resume", async (
    string id,
    HttpContext httpContext,
    ClaimsPrincipal principal,
    IFixedRulesService service,
    FixedRulesMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.Resumed(current.StatusCode, "auth", hasIfMatch: false);
        return current.Error;
    }

    var ifMatch = httpContext.Request.Headers.IfMatch.ToString();
    var response = await service.ResumeAsync(
        current.UserId!,
        id,
        string.IsNullOrWhiteSpace(ifMatch) ? null : ifMatch,
        cancellationToken);
    metrics.Resumed(StatusCodes.Status200OK, "ok", !string.IsNullOrWhiteSpace(ifMatch));
    return Results.Ok(response);
}).RequireRateLimiting("fixed-rules-patch");

fixedRules.MapDelete("{id}", async (
    string id,
    HttpContext httpContext,
    ClaimsPrincipal principal,
    IFixedRulesService service,
    FixedRulesMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.Deleted(current.StatusCode, "auth", hasIfMatch: false);
        return current.Error;
    }

    var ifMatch = httpContext.Request.Headers.IfMatch.ToString();
    await service.SoftDeleteAsync(
        current.UserId!,
        id,
        string.IsNullOrWhiteSpace(ifMatch) ? null : ifMatch,
        cancellationToken);
    metrics.Deleted(StatusCodes.Status204NoContent, "ok", !string.IsNullOrWhiteSpace(ifMatch));
    return Results.NoContent();
}).RequireRateLimiting("fixed-rules-delete");

fixedRules.MapGet("{id}/preview", async (
    string id,
    string? from,
    string? to,
    ClaimsPrincipal principal,
    IFixedRulesService service,
    FixedRulesMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.PreviewRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await service.PreviewAsync(current.UserId!, id, from, to, cancellationToken);
    metrics.PreviewRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("fixed-rules-preview");

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

static (string? UserId, IResult? Error, int StatusCode) ResolveCurrent(ClaimsPrincipal principal)
{
    var userId = CurrentUserId(principal);
    return UserIdRules.IsValid(userId)
        ? (userId, null, StatusCodes.Status200OK)
        : (null, ProblemHttp.Unauthorized(), StatusCodes.Status401Unauthorized);
}

static string? CurrentUserId(ClaimsPrincipal principal) =>
    principal.FindFirst("userId")?.Value
    ?? principal.FindFirst("sub")?.Value
    ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

public partial class Program;
