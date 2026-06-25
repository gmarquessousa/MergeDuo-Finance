using System.Security.Claims;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using MergeDuo.Aggregates.Api;
using MergeDuo.Aggregates.Api.Security;
using MergeDuo.Aggregates.Domain.Abstractions;
using MergeDuo.Aggregates.Domain.Exceptions;
using MergeDuo.Aggregates.Domain.Options;
using MergeDuo.Aggregates.Domain.Rules;
using MergeDuo.Aggregates.Domain.Services;
using MergeDuo.Aggregates.Infra.Cosmos;
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
var aggregatesOptions = Bind<AggregatesOptions>("Aggregates");
var changeFeedOptions = Bind<ChangeFeedOptions>("ChangeFeed");
var rateLimitOptions = Bind<RateLimitOptions>("RateLimit");

builder.Services.AddSingleton(cosmosOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(corsOptions);
builder.Services.AddSingleton(aggregatesOptions);
builder.Services.AddSingleton(changeFeedOptions);
builder.Services.AddSingleton(rateLimitOptions);

builder.Services.AddSingleton<AggregatesMetrics>();
builder.Services.AddSingleton<ICosmosDiagnosticsRecorder>(sp => sp.GetRequiredService<AggregatesMetrics>());

builder.Services.AddSingleton<CosmosClient>(sp => CosmosClientFactory.Create(sp.GetRequiredService<CosmosOptions>()));
builder.Services.AddSingleton<IMonthlyAggregatesRepository, MonthlyAggregatesRepository>();
builder.Services.AddSingleton<ITransactionsProjectionRepository, TransactionsProjectionRepository>();
builder.Services.AddSingleton<IPartnershipsReadRepository, PartnershipsReadRepository>();
builder.Services.AddSingleton<IUsersReadRepository, UsersReadRepository>();
builder.Services.AddSingleton<IFixedRulesProjectionRepository, FixedRulesProjectionRepository>();
builder.Services.AddSingleton<ICardsProjectionRepository, CardsProjectionRepository>();
builder.Services.AddSingleton<IAggregatesReadinessProbe, CosmosReadinessProbe>();
builder.Services.AddSingleton<AggregateCalculator>();
builder.Services.AddSingleton<AggregateRebuildPlanner>();
builder.Services.AddSingleton<FixedRuleProjectionService>();
builder.Services.AddSingleton<IAggregateQueryService, AggregateQueryService>();
builder.Services.AddSingleton<IAggregateRecomputeService, AggregateRecomputeService>();
builder.Services.AddHostedService<TransactionsChangeFeedHostedService>();
builder.Services.AddHostedService<PartnershipsChangeFeedHostedService>();
builder.Services.AddHostedService<FixedRulesChangeFeedHostedService>();

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
        FixedWindow(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", rateLimitOptions.GlobalPermitLimit));

    options.AddPolicy("aggregates-month", context => FixedWindow(RateLimitKey(context), rateLimitOptions.MonthPermitLimit));
    options.AddPolicy("aggregates-year", context => FixedWindow(RateLimitKey(context), rateLimitOptions.YearPermitLimit));
    options.AddPolicy("aggregates-partner-month", context => FixedWindow(RateLimitKey(context), rateLimitOptions.PartnerMonthPermitLimit));
    options.AddPolicy("aggregates-partner-year", context => FixedWindow(RateLimitKey(context), rateLimitOptions.PartnerYearPermitLimit));
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
        .WithMetrics(metrics => metrics.AddMeter(AggregatesMetrics.MeterName));
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
            AggregatesBadRequestException ex => (StatusCodes.Status400BadRequest, ex.Code, ex.Message),
            AggregatesForbiddenException ex => (StatusCodes.Status403Forbidden, ex.Code, ex.Message),
            AggregatesConflictException ex => (StatusCodes.Status409Conflict, ex.Code, ex.Message),
            InvalidTransactionProjectionException ex => (StatusCodes.Status422UnprocessableEntity, ex.Code, ex.Message),
            AggregatesDependencyException => (StatusCodes.Status503ServiceUnavailable, "aggregates_dependency_unavailable", "Aggregates dependency unavailable."),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            ArgumentException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            _ => (StatusCodes.Status500InternalServerError, "aggregates_unhandled_error", "Unhandled aggregates error.")
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
app.MapGet("/readyz", async (IAggregatesReadinessProbe readiness, CancellationToken cancellationToken) =>
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

var aggregates = app.MapGroup("/aggregates").RequireAuthorization();

aggregates.MapGet("/me/{year:int}/{month:int}", async (
    int year,
    int month,
    ClaimsPrincipal principal,
    IAggregateQueryService service,
    AggregatesMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.MonthRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var yearMonth = YearMonth.FromRoute(year, month);
    var response = await service.GetMonthAsync(current.UserId!, current.UserId!, yearMonth, cancellationToken);
    metrics.ResponseShape(response.Source, response.IsStale);
    metrics.MonthRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("aggregates-month");

aggregates.MapGet("/me/year/{year:int}", async (
    int year,
    ClaimsPrincipal principal,
    IAggregateQueryService service,
    AggregatesMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.YearRequested(current.StatusCode, "auth");
        return current.Error;
    }

    _ = YearMonth.FromRoute(year, 1);
    var response = await service.GetYearAsync(current.UserId!, current.UserId!, year, cancellationToken);
    foreach (var month in response.Months)
    {
        metrics.ResponseShape(month.Source, month.IsStale);
    }

    metrics.YearRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("aggregates-year");

aggregates.MapGet("/{userId}/{year:int}/{month:int}", async (
    string userId,
    int year,
    int month,
    ClaimsPrincipal principal,
    IAggregateQueryService service,
    AggregatesMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.MonthRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var yearMonth = YearMonth.FromRoute(year, month);
    var response = await service.GetMonthAsync(current.UserId!, userId, yearMonth, cancellationToken);
    metrics.ResponseShape(response.Source, response.IsStale);
    metrics.MonthRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("aggregates-partner-month");

aggregates.MapPost("/me/backfill/{year:int}", async (
    int year,
    ClaimsPrincipal principal,
    IAggregateRecomputeService recompute,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        return current.Error;
    }

    _ = YearMonth.FromRoute(year, 1);
    await recompute.BackfillYearAsync(current.UserId!, year, cancellationToken);
    return Results.Ok(new { status = "ok", userId = current.UserId, year });
}).RequireRateLimiting("aggregates-year");

aggregates.MapPost("/me/backfill/{year:int}/{month:int}", async (
    int year,
    int month,
    ClaimsPrincipal principal,
    IAggregateRecomputeService recompute,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        return current.Error;
    }

    var yearMonth = YearMonth.FromRoute(year, month);
    await recompute.RecomputeMonthAsync(current.UserId!, yearMonth, cancellationToken);
    return Results.Ok(new { status = "ok", userId = current.UserId, year, month });
}).RequireRateLimiting("aggregates-month");

aggregates.MapGet("/{userId}/year/{year:int}", async (
    string userId,
    int year,
    ClaimsPrincipal principal,
    IAggregateQueryService service,
    AggregatesMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.YearRequested(current.StatusCode, "auth");
        return current.Error;
    }

    _ = YearMonth.FromRoute(year, 1);
    var response = await service.GetYearAsync(current.UserId!, userId, year, cancellationToken);
    foreach (var month in response.Months)
    {
        metrics.ResponseShape(month.Source, month.IsStale);
    }

    metrics.YearRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("aggregates-partner-year");

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
