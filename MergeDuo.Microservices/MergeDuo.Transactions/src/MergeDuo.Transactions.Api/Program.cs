using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using MergeDuo.Transactions.Api;
using MergeDuo.Transactions.Api.Security;
using MergeDuo.Transactions.Domain.Abstractions;
using MergeDuo.Transactions.Domain.Contracts;
using MergeDuo.Transactions.Domain.Exceptions;
using MergeDuo.Transactions.Domain.Options;
using MergeDuo.Transactions.Domain.Rules;
using MergeDuo.Transactions.Domain.Services;
using MergeDuo.Transactions.Infra.Cosmos;
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
var transactionsOptions = Bind<TransactionsOptions>("Transactions");
var internalApiOptions = Bind<InternalApiOptions>("InternalApi");
var rateLimitOptions = Bind<RateLimitOptions>("RateLimit");

builder.Services.AddSingleton(cosmosOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(corsOptions);
builder.Services.AddSingleton(transactionsOptions);
builder.Services.AddSingleton(internalApiOptions);
builder.Services.AddSingleton(rateLimitOptions);

builder.Services.AddSingleton<TransactionsMetrics>();
builder.Services.AddSingleton<ICosmosDiagnosticsRecorder>(sp => sp.GetRequiredService<TransactionsMetrics>());

builder.Services.AddSingleton<CosmosClient>(sp => CosmosClientFactory.Create(sp.GetRequiredService<CosmosOptions>()));
builder.Services.AddSingleton<ITransactionsRepository, TransactionsRepository>();
builder.Services.AddSingleton<ICardsReadRepository, CardsReadRepository>();
builder.Services.AddSingleton<IFixedRulesReadRepository, FixedRulesReadRepository>();
builder.Services.AddSingleton<IPartnershipsReadRepository, PartnershipsReadRepository>();
builder.Services.AddSingleton<ITransactionsReadinessProbe, CosmosReadinessProbe>();
builder.Services.AddSingleton<ITransactionIdGenerator, UlidTransactionIdGenerator>();
builder.Services.AddSingleton<ITransactionsService, TransactionsService>();

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

    options.AddPolicy("transactions-list", context => FixedWindow(RateLimitKey(context), rateLimitOptions.ListPermitLimit));
    options.AddPolicy("transactions-get", context => FixedWindow(RateLimitKey(context), rateLimitOptions.GetPermitLimit));
    options.AddPolicy("transactions-create", context => FixedWindow(RateLimitKey(context), rateLimitOptions.CreatePermitLimit));
    options.AddPolicy("transactions-patch", context => FixedWindow(RateLimitKey(context), rateLimitOptions.PatchPermitLimit));
    options.AddPolicy("transactions-delete", context => FixedWindow(RateLimitKey(context), rateLimitOptions.DeletePermitLimit));
    options.AddPolicy("transactions-group-read", context => FixedWindow(RateLimitKey(context), rateLimitOptions.GroupReadPermitLimit));
    options.AddPolicy("transactions-group-delete", context => FixedWindow(RateLimitKey(context), rateLimitOptions.GroupDeletePermitLimit));
    options.AddPolicy("transactions-card-usage", context => FixedWindow(RateLimitKey(context), rateLimitOptions.CardUsagePermitLimit));
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
        .WithMetrics(metrics => metrics.AddMeter(TransactionsMetrics.MeterName));
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
            TransactionsBadRequestException ex when ex.Code == "unauthorized" => (StatusCodes.Status401Unauthorized, "unauthorized", "JWT is missing, invalid or expired."),
            TransactionsBadRequestException ex => (StatusCodes.Status400BadRequest, ex.Code, ex.Message),
            TransactionsForbiddenException ex => (StatusCodes.Status403Forbidden, ex.Code, ex.Message),
            TransactionsNotFoundException ex => (StatusCodes.Status404NotFound, ex.Code, ex.Message),
            TransactionsConflictException ex => (StatusCodes.Status409Conflict, ex.Code, ex.Message),
            TransactionsPreconditionFailedException ex => (StatusCodes.Status412PreconditionFailed, ex.Code, ex.Message),
            TransactionsDependencyException ex => (StatusCodes.Status503ServiceUnavailable, ex.Code, "Transactions dependency unavailable."),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            ArgumentException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            _ => (StatusCodes.Status500InternalServerError, "transactions_unhandled_error", "Unhandled transactions error.")
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
app.MapGet("/readyz", async (ITransactionsReadinessProbe readiness, CancellationToken cancellationToken) =>
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

var transactions = app.MapGroup("/transactions").RequireAuthorization();

transactions.MapGet("", async (
    string? ym,
    string? category,
    string? cardId,
    string? owner,
    int? pageSize,
    string? continuationToken,
    string? sort,
    ClaimsPrincipal principal,
    ITransactionsService service,
    TransactionsMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.ListRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await service.ListAsync(current.UserId!, ym ?? "", category, cardId, owner, pageSize, continuationToken, sort, cancellationToken);
    metrics.ListRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("transactions-list");

transactions.MapPost("", async (
    CreateTransactionRequest? request,
    HttpContext httpContext,
    ClaimsPrincipal principal,
    ITransactionsService service,
    TransactionsMetrics metrics,
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

    var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
    var response = await service.CreateAsync(current.UserId!, request, string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey, cancellationToken);
    metrics.Created(StatusCodes.Status201Created, "ok");
    return Results.Created($"/transactions/{response.Items[0].Id}?ym={response.Items[0].YearMonth}", response);
}).RequireRateLimiting("transactions-create");

transactions.MapGet("tags", async (
    bool? includeTransactions,
    ClaimsPrincipal principal,
    ITransactionsService service,
    TransactionsMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.GetRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await service.GetTagAnalyticsAsync(current.UserId!, includeTransactions == true, cancellationToken);
    metrics.GetRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("transactions-list");

transactions.MapGet("tags/suggestions", async (
    string? prefix,
    int? limit,
    ClaimsPrincipal principal,
    ITransactionsService service,
    TransactionsMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.GetRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await service.GetTagSuggestionsAsync(current.UserId!, prefix, limit, cancellationToken);
    metrics.GetRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("transactions-list");

transactions.MapGet("groups/{groupId}", async (
    string groupId,
    string? ownerUserId,
    ClaimsPrincipal principal,
    ITransactionsService service,
    TransactionsMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.GetRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await service.GetGroupAsync(current.UserId!, groupId, ownerUserId, cancellationToken);
    metrics.GetRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("transactions-group-read");

transactions.MapDelete("groups/{groupId}", async (
    string groupId,
    ClaimsPrincipal principal,
    ITransactionsService service,
    TransactionsMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.GroupDeleted(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await service.DeleteGroupAsync(current.UserId!, groupId, cancellationToken);
    metrics.GroupDeleted(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("transactions-group-delete");

transactions.MapGet("{id}", async (
    string id,
    string? ym,
    string? ownerUserId,
    ClaimsPrincipal principal,
    ITransactionsService service,
    TransactionsMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.GetRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await service.GetAsync(current.UserId!, id, ym ?? "", ownerUserId, cancellationToken);
    metrics.GetRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("transactions-get");

transactions.MapPatch("{id}", async (
    string id,
    string? ym,
    UpdateTransactionRequest? request,
    HttpContext httpContext,
    ClaimsPrincipal principal,
    ITransactionsService service,
    TransactionsMetrics metrics,
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
    var response = await service.PatchAsync(current.UserId!, id, ym ?? "", request, string.IsNullOrWhiteSpace(ifMatch) ? null : ifMatch, cancellationToken);
    metrics.Updated(StatusCodes.Status200OK, "ok", !string.IsNullOrWhiteSpace(ifMatch));
    return Results.Ok(response);
}).RequireRateLimiting("transactions-patch");

transactions.MapDelete("{id}", async (
    string id,
    string? ym,
    HttpContext httpContext,
    ClaimsPrincipal principal,
    ITransactionsService service,
    TransactionsMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.Deleted(current.StatusCode, "auth", hasIfMatch: false);
        return current.Error;
    }

    var ifMatch = httpContext.Request.Headers.IfMatch.ToString();
    await service.SoftDeleteAsync(current.UserId!, id, ym ?? "", string.IsNullOrWhiteSpace(ifMatch) ? null : ifMatch, cancellationToken);
    metrics.Deleted(StatusCodes.Status204NoContent, "ok", !string.IsNullOrWhiteSpace(ifMatch));
    return Results.NoContent();
}).RequireRateLimiting("transactions-delete");

var internalTransactions = app.MapGroup("/internal/transactions").RequireAuthorization();

internalTransactions.MapGet("card-usage", async (
    string? cardId,
    string? ym,
    ClaimsPrincipal principal,
    ITransactionsService service,
    TransactionsMetrics metrics,
    CancellationToken cancellationToken) =>
{
    var current = ResolveCurrent(principal);
    if (current.Error is not null)
    {
        metrics.CardUsageRequested(current.StatusCode, "auth");
        return current.Error;
    }

    var response = await service.GetCardUsageAsync(current.UserId!, cardId ?? "", ym ?? "", cancellationToken);
    metrics.CardUsageRequested(StatusCodes.Status200OK, "ok");
    return Results.Ok(response);
}).RequireRateLimiting("transactions-card-usage");

var scheduler = app.MapGroup("/internal/scheduler").WithTags("Internal Scheduler");

scheduler.MapPost("transactions", async (
    InternalCreateTransactionRequest? request,
    HttpContext httpContext,
    ITransactionsService service,
    TransactionsMetrics metrics,
    InternalApiOptions internalApi,
    CancellationToken cancellationToken) =>
{
    if (!IsValidInternalKey(httpContext, internalApi))
    {
        metrics.Created(StatusCodes.Status401Unauthorized, "internal_auth");
        return ProblemHttp.Problem(StatusCodes.Status401Unauthorized, "unauthorized", "Invalid internal API key.");
    }

    if (request?.Transaction is null || string.IsNullOrWhiteSpace(request.UserId))
    {
        metrics.ValidationFailed("invalid_request");
        return ProblemHttp.InvalidRequest("Request body is required.");
    }

    if (request.ExtraFields is { Count: > 0 })
    {
        metrics.ValidationFailed("invalid_request");
        return ProblemHttp.InvalidRequest("Request contains unsupported fields.");
    }

    var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
    var response = await service.CreateAsync(
        request.UserId,
        request.Transaction,
        string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
        cancellationToken);
    metrics.Created(StatusCodes.Status201Created, "ok");
    return Results.Created($"/transactions/{response.Items[0].Id}?ym={response.Items[0].YearMonth}", response);
});

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

static bool IsValidInternalKey(HttpContext context, InternalApiOptions options)
{
    var expected = options.SchedulerKey;
    var provided = context.Request.Headers["X-MergeDuo-Internal-Key"].ToString();
    if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(provided))
    {
        return false;
    }

    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    var providedBytes = Encoding.UTF8.GetBytes(provided);
    return expectedBytes.Length == providedBytes.Length &&
        CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
}

public partial class Program;
