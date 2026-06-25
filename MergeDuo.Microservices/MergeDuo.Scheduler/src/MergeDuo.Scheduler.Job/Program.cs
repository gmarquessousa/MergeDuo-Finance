using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

var environment =
    Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    ?? "Production";

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var cosmosOptions = configuration.GetSection("Cosmos").Get<CosmosOptions>() ?? new CosmosOptions();
var schedulerOptions = configuration.GetSection("Scheduler").Get<SchedulerOptions>() ?? new SchedulerOptions();
var transactionsOptions = configuration.GetSection("TransactionsService").Get<TransactionsServiceOptions>() ?? new TransactionsServiceOptions();

if (!schedulerOptions.DryRun && string.IsNullOrWhiteSpace(transactionsOptions.InternalKey))
{
    throw new InvalidOperationException("TransactionsService:InternalKey must be configured when Scheduler:DryRun is false.");
}

using var cosmos = CosmosClientFactory.Create(cosmosOptions);
var fixedRules = cosmos.GetContainer(cosmosOptions.Database, cosmosOptions.FixedRulesContainer);
using var transactionsClient = SchedulerCore.CreateTransactionsClient(transactionsOptions);

var now = DateTimeOffset.UtcNow;
var today = SchedulerCore.BusinessToday(now, schedulerOptions.BusinessTimeZone);
var dueRules = await SchedulerCore.ListDueRulesAsync(fixedRules, today, schedulerOptions.MaxRulesPerRun, CancellationToken.None);

var created = 0;
var skipped = 0;
foreach (var rule in dueRules)
{
    var dueOccurrence = SchedulerCore.ResolveDueOccurrence(rule, today);
    if (dueOccurrence is null)
    {
        skipped++;
        continue;
    }

    if (!schedulerOptions.DryRun)
    {
        var processed = await SchedulerCore.ProcessDueRuleAsync(
            rule,
            dueOccurrence.Value,
            ct => SchedulerCore.CreateTransactionAsync(transactionsClient, transactionsOptions, rule, dueOccurrence.Value, ct),
            (nextRunAt, ct) => SchedulerCore.PatchRuleCheckpointAsync(fixedRules, rule, dueOccurrence.Value, nextRunAt, now, ct),
            CancellationToken.None);

        if (processed)
        {
            created++;
        }
    }
}

Console.WriteLine($"Scheduler completed. dueRules={dueRules.Count} created={created} skipped={skipped} dryRun={schedulerOptions.DryRun}");

public static class SchedulerCore
{
public static async Task<IReadOnlyList<FixedRuleDocument>> ListDueRulesAsync(
    Container fixedRules,
    DateOnly today,
    int maxRules,
    CancellationToken cancellationToken)
{
    var query = new QueryDefinition(
            """
            SELECT TOP @limit *
            FROM c
            WHERE c.docType = 'fixedRule'
              AND c.active = true
              AND (NOT IS_DEFINED(c.deletedAt) OR IS_NULL(c.deletedAt))
              AND c.startsAt <= @today
                            AND (NOT IS_DEFINED(c.endsAt) OR IS_NULL(c.endsAt) OR c.endsAt >= @monthStart)
                            AND (NOT IS_DEFINED(c.nextRunAt) OR IS_NULL(c.nextRunAt) OR c.nextRunAt <= @today)
            """)
        .WithParameter("@limit", Math.Clamp(maxRules, 1, 10_000))
                .WithParameter("@monthStart", FormatDate(new DateOnly(today.Year, today.Month, 1)))
        .WithParameter("@today", FormatDate(today));

    using var iterator = fixedRules.GetItemQueryIterator<FixedRuleDocument>(
        query,
        requestOptions: new QueryRequestOptions { MaxItemCount = Math.Clamp(maxRules, 1, 500) });

    var results = new List<FixedRuleDocument>();
    while (iterator.HasMoreResults && results.Count < maxRules)
    {
        var page = await iterator.ReadNextAsync(cancellationToken);
        results.AddRange(page.Resource.Take(maxRules - results.Count));
    }

    return results;
}

public static bool IsDueToday(FixedRuleDocument rule, DateOnly today)
{
    return ResolveDueOccurrence(rule, today) == today;
}

public static DateOnly? ResolveDueOccurrence(FixedRuleDocument rule, DateOnly today)
{
    var startsAt = ParseDate(rule.StartsAt);
    if (today < startsAt)
    {
        return null;
    }

    var endsAt = string.IsNullOrWhiteSpace(rule.EndsAt)
        ? (DateOnly?)null
        : ParseDate(rule.EndsAt);

    if (endsAt is not null && today < startsAt)
    {
        return null;
    }

    if (!string.IsNullOrWhiteSpace(rule.NextRunAt))
    {
        var nextRunAt = ParseDate(rule.NextRunAt);
        if (nextRunAt < startsAt || nextRunAt > today)
        {
            return null;
        }

        return endsAt is not null && nextRunAt > endsAt.Value
            ? null
            : nextRunAt;
    }

    var occurrence = ResolveOccurrenceDate(rule.Schedule, today.Year, today.Month);
    if (occurrence > today || occurrence < startsAt)
    {
        return null;
    }

    if (!string.IsNullOrWhiteSpace(rule.LastRunAt) && ParseDate(rule.LastRunAt) >= occurrence)
    {
        return null;
    }

    return endsAt is not null && occurrence > endsAt.Value
        ? null
        : occurrence;
}

public static async Task<bool> ProcessDueRuleAsync(
    FixedRuleDocument rule,
    DateOnly occurrenceDate,
    Func<CancellationToken, Task> createTransactionAsync,
    Func<DateOnly?, CancellationToken, Task> updateCheckpointAsync,
    CancellationToken cancellationToken)
{
    if (!IsDueToday(rule, occurrenceDate))
    {
        return false;
    }

    await createTransactionAsync(cancellationToken);
    await updateCheckpointAsync(NextOccurrence(rule, occurrenceDate), cancellationToken);
    return true;
}

public static HttpClient CreateTransactionsClient(TransactionsServiceOptions options)
{
    if (string.IsNullOrWhiteSpace(options.BaseUrl))
    {
        throw new InvalidOperationException("TransactionsService:BaseUrl must be configured.");
    }

    return new HttpClient
    {
        BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds))
    };
}

public static async Task<CreateTransactionsResponse> CreateTransactionAsync(
    HttpClient client,
    TransactionsServiceOptions options,
    FixedRuleDocument rule,
    DateOnly occurrenceDate,
    CancellationToken cancellationToken)
{
    var transaction = BuildTransactionRequest(rule, occurrenceDate);
    using var request = new HttpRequestMessage(HttpMethod.Post, "internal/scheduler/transactions")
    {
        Content = JsonContent.Create(new InternalCreateTransactionRequest(rule.UserId, transaction))
    };
    request.Headers.TryAddWithoutValidation("X-MergeDuo-Internal-Key", options.InternalKey);
    request.Headers.TryAddWithoutValidation("Idempotency-Key", $"fixed-rule:{rule.Id}:{FormatDate(occurrenceDate)}");

    using var response = await client.SendAsync(request, cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Transactions returned {(int)response.StatusCode}: {body}");
    }

    return (await response.Content.ReadFromJsonAsync<CreateTransactionsResponse>(cancellationToken))!;
}

public static CreateTransactionRequest BuildTransactionRequest(FixedRuleDocument rule, DateOnly occurrenceDate) =>
    new(
        Date: rule.Category == Categories.CreditCard ? null : occurrenceDate,
        PurchaseDate: rule.Category == Categories.CreditCard ? occurrenceDate : null,
        Category: rule.Category,
        Description: rule.Description,
        Amount: rule.Amount,
        Currency: "BRL",
        CardId: rule.Category == Categories.CreditCard ? rule.CardId : null,
        Tags: rule.Tags ?? [],
        FixedRuleId: rule.Id);

public static Task PatchRuleCheckpointAsync(
    Container fixedRules,
    FixedRuleDocument rule,
    DateOnly lastRunAt,
    DateOnly? nextRunAt,
    DateTimeOffset updatedAt,
    CancellationToken cancellationToken)
{
    var operations = new List<PatchOperation>
    {
        PatchOperation.Set("/lastRunAt", FormatDate(lastRunAt)),
        PatchOperation.Set<string?>("/nextRunAt", nextRunAt is null ? null : FormatDate(nextRunAt.Value)),
        PatchOperation.Set("/updatedAt", updatedAt)
    };

    return fixedRules.PatchItemAsync<FixedRuleDocument>(
        rule.Id,
        new PartitionKey(rule.UserId),
        operations,
        cancellationToken: cancellationToken);
}

public static DateOnly? NextOccurrence(FixedRuleDocument rule, DateOnly after)
{
    var cursor = new DateOnly(after.Year, after.Month, 1).AddMonths(1);
    for (var i = 0; i < 120; i++)
    {
        var occurrence = ResolveOccurrenceDate(rule.Schedule, cursor.Year, cursor.Month);
        if (string.IsNullOrWhiteSpace(rule.EndsAt) || occurrence <= ParseDate(rule.EndsAt))
        {
            return occurrence;
        }

        cursor = cursor.AddMonths(1);
    }

    return null;
}

public static DateOnly ResolveOccurrenceDate(FixedRuleScheduleDocument schedule, int year, int month)
{
    var day = schedule.Type switch
    {
        "calendar_day" => Math.Min(schedule.Day!.Value, DateTime.DaysInMonth(year, month)),
        "business_day" => NthBusinessDay(year, month, schedule.Ordinal!.Value).Day,
        "period" when schedule.Period == "start" => 1,
        "period" when schedule.Period == "middle" => Math.Min(15, DateTime.DaysInMonth(year, month)),
        "period" when schedule.Period == "end" => DateTime.DaysInMonth(year, month),
        _ => throw new InvalidOperationException("Invalid fixed rule schedule.")
    };

    return new DateOnly(year, month, day);
}

public static DateOnly NthBusinessDay(int year, int month, int ordinal)
{
    var lastBusinessDay = new DateOnly(year, month, 1);
    var count = 0;
    for (var day = 1; day <= DateTime.DaysInMonth(year, month); day++)
    {
        var candidate = new DateOnly(year, month, day);
        if (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            continue;
        }

        count++;
        lastBusinessDay = candidate;
        if (count == ordinal)
        {
            return candidate;
        }
    }

    return lastBusinessDay;
}

public static DateOnly BusinessToday(DateTimeOffset now, string timeZoneId)
{
    var timeZone = ResolveTimeZone(timeZoneId);
    var local = TimeZoneInfo.ConvertTime(now, timeZone);
    return DateOnly.FromDateTime(local.DateTime);
}

public static TimeZoneInfo ResolveTimeZone(string timeZoneId)
{
    try
    {
        return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }
    catch (TimeZoneNotFoundException) when (timeZoneId == "America/Sao_Paulo")
    {
        return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
    }
    catch (InvalidTimeZoneException) when (timeZoneId == "America/Sao_Paulo")
    {
        return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
    }
}

public static DateOnly ParseDate(string date) => DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string Database { get; set; } = "mergeduo";
    public string FixedRulesContainer { get; set; } = "fixedRules";
}

public sealed class SchedulerOptions
{
    public string BusinessTimeZone { get; set; } = "America/Sao_Paulo";
    public int MaxRulesPerRun { get; set; } = 500;
    public bool DryRun { get; set; }
}

public sealed class TransactionsServiceOptions
{
    public string BaseUrl { get; set; } = "https://localhost:7282";
    public int TimeoutSeconds { get; set; } = 5;
    public string InternalKey { get; set; } = "";
}

public static class CosmosClientFactory
{
    public static CosmosClient Create(CosmosOptions options)
    {
        var clientOptions = new CosmosClientOptions
        {
            ApplicationName = "mergeduo-scheduler",
            ConsistencyLevel = ConsistencyLevel.Session,
            ConnectionMode = ConnectionMode.Direct,
            EnableContentResponseOnWrite = false,
            MaxRetryAttemptsOnRateLimitedRequests = 9,
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        };

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new CosmosClient(options.ConnectionString, clientOptions);
        }

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return new CosmosClient(options.Endpoint, new DefaultAzureCredential(), clientOptions);
        }

        throw new InvalidOperationException("Cosmos:Endpoint or Cosmos:ConnectionString must be configured.");
    }
}

public sealed class FixedRuleDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "fixedRule";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = [];

    [JsonPropertyName("schedule")]
    public FixedRuleScheduleDocument Schedule { get; set; } = new();

    [JsonPropertyName("startsAt")]
    public string StartsAt { get; set; } = "";

    [JsonPropertyName("endsAt")]
    public string? EndsAt { get; set; }

    [JsonPropertyName("lastRunAt")]
    public string? LastRunAt { get; set; }

    [JsonPropertyName("nextRunAt")]
    public string? NextRunAt { get; set; }
}

public sealed class FixedRuleScheduleDocument
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("day")]
    public int? Day { get; set; }

    [JsonPropertyName("ordinal")]
    public int? Ordinal { get; set; }

    [JsonPropertyName("period")]
    public string? Period { get; set; }
}

public sealed record InternalCreateTransactionRequest(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("transaction")] CreateTransactionRequest Transaction);

public sealed record CreateTransactionRequest(
    [property: JsonPropertyName("date")] DateOnly? Date,
    [property: JsonPropertyName("purchaseDate")] DateOnly? PurchaseDate,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("cardId")] string? CardId,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("fixedRuleId")] string FixedRuleId);

public sealed record CreateTransactionsResponse(
    [property: JsonPropertyName("groupId")] string? GroupId,
    [property: JsonPropertyName("items")] IReadOnlyList<TransactionResponse> Items);

public sealed record TransactionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("yearMonth")] string YearMonth);

public static class Categories
{
    public const string Income = "income";
    public const string CreditCard = "credit_card";
    public const string Loan = "loan";
    public const string FixedExpense = "fixed_expense";
    public const string VariableExpense = "variable_expense";
    public const string Investment = "investment";
}
