using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MergeDuo.Aggregates.Domain.Contracts;
using MergeDuo.Aggregates.Domain.Documents;

namespace MergeDuo.Aggregates.Tests;

public sealed class ApiFlowTests
{
    [Fact]
    public async Task Protected_endpoint_rejects_missing_jwt()
    {
        using var factory = new TestAggregatesFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/aggregates/me/2026/4");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Me_month_returns_stored_aggregate_without_cosmos_metadata()
    {
        using var factory = new TestAggregatesFactory();
        using var client = factory.CreateHttpsClient();
        factory.Aggregates.Seed(Aggregate("usr_gmarques", 2026, 4));

        var response = await SendAsync(client, HttpMethod.Get, "/aggregates/me/2026/4", factory.IssueToken("usr_gmarques"));
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, json);
        Assert.Contains("byCategory", json);
        Assert.DoesNotContain("_etag", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_rid", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnostics", json, StringComparison.OrdinalIgnoreCase);
        var aggregate = (await response.Content.ReadFromJsonAsync<MonthlyAggregateResponse>())!;
        Assert.Equal("stored", aggregate.Source);
        Assert.False(aggregate.IsStale);
        Assert.Equal(4115, aggregate.Totals.Saldo);
        Assert.Equal(30, aggregate.DailyBalances.Count);
        Assert.Equal(4115, aggregate.DailyBalances[^1].Saldo);
        Assert.Single(aggregate.DailyMovements);
        Assert.Equal("tx_income", aggregate.DailyMovements[0].Id);
    }

    [Fact]
    public async Task Missing_month_returns_carried_month_without_persisting()
    {
        using var factory = new TestAggregatesFactory();
        using var client = factory.CreateHttpsClient();

        var response = await SendAsync(client, HttpMethod.Get, "/aggregates/me/2026/2", factory.IssueToken("usr_gmarques"));
        response.EnsureSuccessStatusCode();
        var aggregate = (await response.Content.ReadFromJsonAsync<MonthlyAggregateResponse>())!;

        Assert.Equal("carried", aggregate.Source);
        Assert.True(aggregate.IsStale);
        Assert.Null(aggregate.ComputedAt);
        Assert.Equal(0, factory.Aggregates.UpsertCount);
        Assert.Equal(DateTime.DaysInMonth(2026, 2), aggregate.DailyBalances.Count);
    }

    [Fact]
    public async Task Future_month_recomputes_on_read_to_supply_daily_balances()
    {
        using var factory = new TestAggregatesFactory();
        using var client = factory.CreateHttpsClient();
        factory.Users.Seed("usr_gmarques", 1000);
        factory.FixedRules.Seed(new FixedRuleDocument
        {
            Id = "fxr_salary",
            DocType = "fixedRule",
            UserId = "usr_gmarques",
            Category = "income",
            Description = "Salary",
            Amount = 2000,
            StartsAt = "2026-05-05",
            Active = true,
            Schedule = new FixedRuleScheduleDocument
            {
                Type = "calendar_day",
                Day = 5
            }
        });

        var response = await SendAsync(client, HttpMethod.Get, "/aggregates/me/2026/5", factory.IssueToken("usr_gmarques"));
        response.EnsureSuccessStatusCode();
        var aggregate = (await response.Content.ReadFromJsonAsync<MonthlyAggregateResponse>())!;

        Assert.Equal("cold_start", aggregate.Source);
        Assert.False(aggregate.IsStale);
        Assert.Equal(31, aggregate.DailyBalances.Count);
        Assert.Equal(1000, aggregate.DailyBalances.Single(x => x.Day == 4).Saldo);
        Assert.Equal(3000, aggregate.DailyBalances.Single(x => x.Day == 5).Saldo);
        Assert.Equal(1, factory.Aggregates.UpsertCount);
    }

    [Fact]
    public async Task Dirty_source_watermark_recomputes_month_on_read()
    {
        using var factory = new TestAggregatesFactory();
        using var client = factory.CreateHttpsClient();
        factory.Users.Seed("usr_gmarques", 0);
        factory.Aggregates.Seed(Aggregate("usr_gmarques", 2026, 4));
        factory.Transactions.Seed(Tx(
            "tx_new",
            "usr_gmarques",
            "2026-04",
            "2026-04-10",
            "income",
            "in",
            100,
            DateTimeOffset.Parse("2026-04-25T14:06:00Z")));

        var response = await SendAsync(client, HttpMethod.Get, "/aggregates/me/2026/4", factory.IssueToken("usr_gmarques"));
        response.EnsureSuccessStatusCode();
        var aggregate = (await response.Content.ReadFromJsonAsync<MonthlyAggregateResponse>())!;

        Assert.Equal("recomputed", aggregate.Source);
        Assert.False(aggregate.IsStale);
        Assert.Equal("fresh", aggregate.Freshness.State);
        Assert.Equal(100, aggregate.Totals.Saldo);
        Assert.Equal(DateTimeOffset.Parse("2026-04-25T14:06:00Z"), aggregate.SourceWatermark.MaxTransactionUpdatedAt);
        Assert.Equal(1, aggregate.SourceWatermark.ActiveTransactionsCount);
    }

    [Fact]
    public async Task Year_endpoint_returns_twelve_ordered_months()
    {
        using var factory = new TestAggregatesFactory();
        using var client = factory.CreateHttpsClient();
        factory.Aggregates.Seed(Aggregate("usr_gmarques", 2026, 4));

        var response = await SendAsync(client, HttpMethod.Get, "/aggregates/me/year/2026", factory.IssueToken("usr_gmarques"));
        response.EnsureSuccessStatusCode();
        var year = (await response.Content.ReadFromJsonAsync<YearAggregatesResponse>())!;

        Assert.Equal(12, year.Months.Count);
        Assert.Equal(1, year.Months[0].Month);
        Assert.Equal(12, year.Months[^1].Month);
        Assert.Equal("stored", year.Months.Single(x => x.Month == 4).Source);
        Assert.Equal("carried", year.Months.Single(x => x.Month == 5).Source);
    }

    [Fact]
    public async Task Partner_routes_require_active_partnership()
    {
        using var factory = new TestAggregatesFactory();
        using var client = factory.CreateHttpsClient();
        factory.Aggregates.Seed(Aggregate("usr_bmarques", 2026, 4));

        var denied = await SendAsync(client, HttpMethod.Get, "/aggregates/usr_bmarques/2026/4", factory.IssueToken("usr_gmarques"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("aggregate_access_denied", await ProblemCodeAsync(denied));

        factory.Partnerships.Seed(new PartnershipDocument
        {
            Id = "pair_usr_bmarques_usr_gmarques_usr_gmarques",
            UserId = "usr_gmarques",
            PartnerUserId = "usr_bmarques",
            Status = "active",
            MergedSince = new DateOnly(2026, 1, 12)
        });

        var allowed = await SendAsync(client, HttpMethod.Get, "/aggregates/usr_bmarques/2026/4", factory.IssueToken("usr_gmarques"));
        allowed.EnsureSuccessStatusCode();
        var aggregate = (await allowed.Content.ReadFromJsonAsync<MonthlyAggregateResponse>())!;
        Assert.Equal("usr_bmarques", aggregate.UserId);
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

    private static MonthlyAggregateDocument Aggregate(string userId, int year, int month)
    {
        var yearMonth = $"{year:D4}-{month:D2}";
        return new MonthlyAggregateDocument
        {
            Id = $"agg_{userId}_{yearMonth}",
            UserId = userId,
            Year = year,
            MonthIdx = month - 1,
            YearMonth = yearMonth,
            Totals = new MonthlyTotalsDocument
            {
                Entradas = 10300,
                Saidas = 4685,
                Aportes = 1500,
                Saldo = 4115,
                Investido = 38450
            },
            SnapshotToday = new SnapshotTodayDocument
            {
                SaldoHoje = 3210.5m,
                InvestidoHoje = 38450,
                PatrimonioHoje = 41660.5m,
                AsOfDate = new DateOnly(2026, 4, 25)
            },
            DailyBalances = Enumerable.Range(1, DateTime.DaysInMonth(year, month))
                .Select(day => new DailyBalanceDocument { Day = day, Saldo = 4115 })
                .ToList(),
            DailyMovements = new List<DailyMovementDocument>
            {
                new()
                {
                    Day = 5,
                    Id = "tx_income",
                    UserId = userId,
                    Category = "income",
                    Kind = "in",
                    Description = "Salary",
                    Amount = 10300,
                    Projected = false
                }
            },
            Projection = new ProjectionDocument
            {
                IncludesProjected = false,
                ProjectedCount = 0,
                AsOfDate = new DateOnly(2026, 4, 25)
            },
            ByCategory = new Dictionary<string, decimal> { ["income"] = 10300 },
            ByCard = new Dictionary<string, decimal> { ["card_nubank_01"] = 1350 },
            ByOwner = new Dictionary<string, OwnerTotalsDocument>
            {
                [userId] = new() { Entradas = 10300, Saidas = 4685, Aportes = 1500 }
            },
            TransactionsCount = 28,
            ComputedAt = DateTimeOffset.Parse("2026-04-25T14:05:00Z"),
            SourceVersion = 4
        };
    }

    private static TransactionProjection Tx(
        string id,
        string userId,
        string yearMonth,
        string date,
        string category,
        string kind,
        decimal amount,
        DateTimeOffset updatedAt) =>
        new()
        {
            Id = id,
            DocType = "transaction",
            UserId = userId,
            YearMonth = yearMonth,
            Date = DateOnly.Parse(date),
            Category = category,
            Description = id,
            Kind = kind,
            Amount = amount,
            Currency = "BRL",
            UpdatedAt = updatedAt
        };
}
