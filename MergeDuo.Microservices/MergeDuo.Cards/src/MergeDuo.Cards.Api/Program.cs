using System.Security.Claims;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using MergeDuo.Cards.Api;
using MergeDuo.Cards.Api.Security;
using MergeDuo.Cards.Domain.Abstractions;
using MergeDuo.Cards.Domain.Contracts;
using MergeDuo.Cards.Domain.Exceptions;
using MergeDuo.Cards.Domain.Options;
using MergeDuo.Cards.Domain.Rules;
using MergeDuo.Cards.Domain.Services;
using MergeDuo.Cards.Infra.Cosmos;
using MergeDuo.Cards.Infra.Transactions;
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
var transactionsOptions = Bind<TransactionsServiceOptions>("TransactionsService");
var rateLimitOptions = Bind<RateLimitOptions>("RateLimit");

builder.Services.AddSingleton(cosmosOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(corsOptions);
builder.Services.AddSingleton(transactionsOptions);
builder.Services.AddSingleton(rateLimitOptions);

builder.Services.AddSingleton<CardsMetrics>();
builder.Services.AddSingleton<ICosmosDiagnosticsRecorder>(sp => sp.GetRequiredService<CardsMetrics>());

builder.Services.AddSingleton<CosmosClient>(sp => CosmosClientFactory.Create(sp.GetRequiredService<CosmosOptions>()));
builder.Services.AddSingleton<ICardsRepository, CardsRepository>();
builder.Services.AddSingleton<ICardsReadinessProbe, CosmosReadinessProbe>();
builder.Services.AddSingleton<ICardIdGenerator, UlidCardIdGenerator>();
builder.Services.AddSingleton<ICardsService, CardsService>();

builder.Services.AddHttpClient<ICardUsageClient, TransactionsUsageClient>((sp, client) =>
{
    var options = sp.GetRequiredService<TransactionsServiceOptions>();
    client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
});

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

    options.AddPolicy("cards-read", context => FixedWindow(RateLimitKey(context), rateLimitOptions.CardReadPermitLimit));
    options.AddPolicy("cards-create", context => FixedWindow(RateLimitKey(context), rateLimitOptions.CardCreatePermitLimit));
    options.AddPolicy("cards-patch", context => FixedWindow(RateLimitKey(context), rateLimitOptions.CardPatchPermitLimit));
    options.AddPolicy("cards-delete", context => FixedWindow(RateLimitKey(context), rateLimitOptions.CardDeletePermitLimit));
    options.AddPolicy("cards-usage", context => FixedWindow(RateLimitKey(context), rateLimitOptions.CardUsagePermitLimit));
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
        .WithMetrics(metrics => metrics.AddMeter(CardsMetrics.MeterName));
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
            CardsBadRequestException ex => (StatusCodes.Status400BadRequest, ex.Code, ex.Message),
            CardsNotFoundException ex => (StatusCodes.Status404NotFound, ex.Code, ex.Message),
            CardsConflictException ex => (StatusCodes.Status409Conflict, ex.Code, ex.Message),
            CardsPreconditionFailedException ex => (StatusCodes.Status412PreconditionFailed, ex.Code, ex.Message),
            CardsDependencyException ex => (StatusCodes.Status503ServiceUnavailable, ex.Code, "Cards dependency unavailable."),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            ArgumentException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            _ => (StatusCodes.Status500InternalServerError, "cards_unhandled_error", "Unhandled cards error.")
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
    ICardsReadinessProbe readiness,
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

var cards = app.MapGroup("/cards").RequireAuthorization();

cards.MapGet("", async (
    bool? includeDeleted,
    ClaimsPrincipal principal,
    ICardsService service,
    CardsMetrics metrics,
    CancellationToken cancellationToken) =>
{
    if (includeDeleted == true)
    {
        metrics.ValidationFailed("include_deleted");
        return ProblemHttp.InvalidRequest("Deleted cards are not available in v1.");
    }

    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.ListRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await service.ListAsync(current.UserId!, cancellationToken);
    metrics.ListRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("cards-read");

cards.MapPost("", async (
    CreateCardRequest? request,
    ClaimsPrincipal principal,
    ICardsService service,
    CardsMetrics metrics,
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
    return Results.Created($"/cards/{response.Id}", response);
}).RequireRateLimiting("cards-create");

cards.MapGet("{id}", async (
    string id,
    ClaimsPrincipal principal,
    ICardsService service,
    CardsMetrics metrics,
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
}).RequireRateLimiting("cards-read");

cards.MapPatch("{id}", async (
    string id,
    UpdateCardRequest? request,
    HttpContext httpContext,
    ClaimsPrincipal principal,
    ICardsService service,
    CardsMetrics metrics,
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
}).RequireRateLimiting("cards-patch");

cards.MapDelete("{id}", async (
    string id,
    HttpContext httpContext,
    ClaimsPrincipal principal,
    ICardsService service,
    CardsMetrics metrics,
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
}).RequireRateLimiting("cards-delete");

cards.MapGet("{id}/usage", async (
    string id,
    string? ym,
    HttpContext httpContext,
    ClaimsPrincipal principal,
    ICardsService service,
    CardsMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.UsageRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var authorization = httpContext.Request.Headers.Authorization.ToString();
    var response = await service.GetUsageAsync(
        current.UserId!,
        id,
        ym ?? "",
        string.IsNullOrWhiteSpace(authorization) ? null : authorization,
        cancellationToken);
    metrics.UsageRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("cards-usage");

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
