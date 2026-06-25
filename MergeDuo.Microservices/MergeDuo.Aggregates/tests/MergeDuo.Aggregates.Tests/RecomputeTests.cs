using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Options;
using MergeDuo.Aggregates.Domain.Rules;
using MergeDuo.Aggregates.Domain.Services;
using MergeDuo.Aggregates.Tests.Fakes;
using MergeDuo.Aggregates.Tests.Support;

namespace MergeDuo.Aggregates.Tests;

public sealed class RecomputeTests
{
    [Fact]
    public async Task Recompute_month_ignores_soft_deleted_transactions_and_keeps_owner_totals_only()
    {
        var harness = Harness();
        harness.Transactions.Seed(Tx("tx_income", "usr_gmarques", "2026-04", "2026-04-05", "income", "in", 1000));
        harness.Transactions.Seed(Tx("tx_deleted", "usr_gmarques", "2026-04", "2026-04-06", "variable_expense", "out", 999, deleted: true));
        harness.Partnerships.Seed(Partnership("usr_gmarques", "usr_bmarques"));
        harness.Transactions.Seed(Tx("tx_partner", "usr_bmarques", "2026-04", "2026-04-07", "variable_expense", "out", 50));

        await harness.Service.RecomputeMonthAsync("usr_gmarques", new YearMonth(2026, 4), CancellationToken.None);

        var stored = harness.Aggregates.Stored("usr_gmarques", new YearMonth(2026, 4))!;
        Assert.Equal(1000, stored.Totals.Entradas);
        Assert.Equal(0, stored.Totals.Saidas);
        Assert.Equal(1, stored.TransactionsCount);
        Assert.Equal(1000, stored.ByOwner["usr_gmarques"].Entradas);
        Assert.Equal(50, stored.ByOwner["usr_bmarques"].Saidas);
    }

    [Fact]
    public async Task Historical_investment_change_rebuilds_following_months()
    {
        var harness = Harness();
        harness.Users.Seed("usr_gmarques", 1000);
        harness.Transactions.Seed(Tx("tx_invest", "usr_gmarques", "2026-03", "2026-03-10", "investment", "invest", 500));
        harness.Transactions.Seed(Tx("tx_income", "usr_gmarques", "2026-04", "2026-04-05", "income", "in", 1000));

        await harness.Service.RecomputeForChangeAsync("usr_gmarques", new YearMonth(2026, 3), CancellationToken.None);

        var march = harness.Aggregates.Stored("usr_gmarques", new YearMonth(2026, 3))!;
        var april = harness.Aggregates.Stored("usr_gmarques", new YearMonth(2026, 4))!;
        Assert.Equal(500, march.Totals.Investido);
        Assert.Equal(500, april.Totals.Investido);
        Assert.Equal(500, march.Totals.Saldo);
        Assert.Equal(1500, april.Totals.Saldo);
        Assert.Equal(4, harness.Aggregates.UpsertCount);
    }

    [Fact]
    public async Task Future_fixed_rule_projection_updates_accumulated_patrimony()
    {
        var harness = Harness();
        harness.Users.Seed("usr_gmarques", 1000);
        harness.FixedRules.Seed(FixedRule("fxr_salary", "usr_gmarques", "income", 2000, "2026-05-05"));
        harness.FixedRules.Seed(FixedRule("fxr_invest", "usr_gmarques", "investment", 500, "2026-05-10"));

        await harness.Service.RecomputeMonthAsync("usr_gmarques", new YearMonth(2026, 5), CancellationToken.None);

        var may = harness.Aggregates.Stored("usr_gmarques", new YearMonth(2026, 5))!;
        Assert.Equal(2000, may.Totals.Entradas);
        Assert.Equal(500, may.Totals.Aportes);
        Assert.Equal(2500, may.Totals.Saldo);
        Assert.Equal(500, may.Totals.Investido);
        Assert.Equal(3000, may.Totals.Saldo + may.Totals.Investido);
        Assert.True(may.Projection.IncludesProjected);
        Assert.Equal(2, may.Projection.ProjectedCount);
        Assert.Equal(2, may.DailyMovements.Count);
        Assert.All(may.DailyMovements, movement => Assert.True(movement.Projected));
        Assert.Contains(may.DailyMovements, movement => movement.FixedRuleId == "fxr_invest" && movement.Day == 10 && movement.Amount == 500);
        Assert.Equal(0, may.TransactionsCount);
    }

    [Fact]
    public async Task Projected_fixed_rule_is_not_duplicated_when_transaction_exists_for_month()
    {
        var harness = Harness();
        harness.Users.Seed("usr_gmarques", 1000);
        harness.FixedRules.Seed(FixedRule("fxr_salary", "usr_gmarques", "income", 2000, "2026-05-05"));
        var actual = Tx("tx_salary", "usr_gmarques", "2026-05", "2026-05-05", "income", "in", 2000);
        actual.FixedRuleId = "fxr_salary";
        harness.Transactions.Seed(actual);

        await harness.Service.RecomputeMonthAsync("usr_gmarques", new YearMonth(2026, 5), CancellationToken.None);

        var may = harness.Aggregates.Stored("usr_gmarques", new YearMonth(2026, 5))!;
        Assert.Equal(2000, may.Totals.Entradas);
        Assert.Equal(3000, may.Totals.Saldo);
        Assert.False(may.Projection.IncludesProjected);
        Assert.Equal(1, may.TransactionsCount);
    }

    [Fact]
    public async Task Current_month_recomputes_include_overdue_fixed_rule_when_no_real_transaction_exists()
    {
        var harness = Harness(DateTimeOffset.Parse("2026-05-07T15:00:00Z"));
        harness.Users.Seed("usr_gmarques", 1000);

        var rent = FixedRule("fxr_rent", "usr_gmarques", "fixed_expense", 200, "2026-05-01");
        rent.Schedule = new FixedRuleScheduleDocument
        {
            Type = "business_day",
            Ordinal = 1
        };
        harness.FixedRules.Seed(rent);

        await harness.Service.RecomputeMonthAsync("usr_gmarques", new YearMonth(2026, 5), CancellationToken.None);

        var may = harness.Aggregates.Stored("usr_gmarques", new YearMonth(2026, 5))!;
        Assert.Equal(200, may.Totals.Saidas);
        Assert.Equal(800, may.Totals.Saldo);
        Assert.True(may.Projection.IncludesProjected);
        Assert.Equal(1, may.Projection.ProjectedCount);
        Assert.Contains(may.DailyMovements, movement =>
            movement.FixedRuleId == "fxr_rent" &&
            movement.Day == 1 &&
            movement.Projected);
    }

    [Fact]
    public async Task Credit_card_fixed_rule_dedupes_by_purchase_occurrence_not_invoice_month()
    {
        var harness = Harness(DateTimeOffset.Parse("2026-01-25T15:00:00Z"));
        harness.Users.Seed("usr_gmarques", 1000);
        harness.Cards.Seed(new CardDocument
        {
            Id = "card_test",
            UserId = "usr_gmarques",
            ClosingDay = 30,
            DueDay = 5
        });
        harness.FixedRules.Seed(FixedRule("fxr_card", "usr_gmarques", "credit_card", 100, "2026-01-31", "card_test"));

        var actual = Tx("tx_card_jan", "usr_gmarques", "2026-03", "2026-03-05", "credit_card", "out", 100);
        actual.FixedRuleId = "fxr_card";
        actual.PurchaseDate = DateOnly.Parse("2026-01-31");
        harness.Transactions.Seed(actual);

        await harness.Service.RecomputeMonthAsync("usr_gmarques", new YearMonth(2026, 3), CancellationToken.None);

        var march = harness.Aggregates.Stored("usr_gmarques", new YearMonth(2026, 3))!;
        Assert.Equal(200, march.Totals.Saidas);
        Assert.Equal(800, march.Totals.Saldo);
        Assert.Equal(1, march.Projection.ProjectedCount);
        Assert.True(march.Projection.IncludesProjected);
        Assert.Equal(1, march.TransactionsCount);
    }

    [Fact]
    public async Task Future_month_marks_projection_when_only_prior_projected_month_affects_balance()
    {
        var harness = Harness();
        harness.Users.Seed("usr_gmarques", 1000);
        var salary = FixedRule("fxr_salary", "usr_gmarques", "income", 2000, "2026-05-05");
        salary.EndsAt = "2026-05-05";
        harness.FixedRules.Seed(salary);

        await harness.Service.RecomputeMonthAsync("usr_gmarques", new YearMonth(2026, 6), CancellationToken.None);

        var june = harness.Aggregates.Stored("usr_gmarques", new YearMonth(2026, 6))!;
        Assert.Equal(0, june.Totals.Entradas);
        Assert.Equal(3000, june.Totals.Saldo);
        Assert.True(june.Projection.IncludesProjected);
        Assert.Equal(1, june.Projection.ProjectedCount);
    }

    [Fact]
    public async Task Recompute_month_persists_daily_balances_ending_at_month_total()
    {
        var harness = Harness();
        harness.Users.Seed("usr_gmarques", 1000);
        harness.Transactions.Seed(Tx("tx_income", "usr_gmarques", "2026-04", "2026-04-05", "income", "in", 1000));
        harness.Transactions.Seed(Tx("tx_expense", "usr_gmarques", "2026-04", "2026-04-10", "variable_expense", "out", 300));

        await harness.Service.RecomputeMonthAsync("usr_gmarques", new YearMonth(2026, 4), CancellationToken.None);

        var april = harness.Aggregates.Stored("usr_gmarques", new YearMonth(2026, 4))!;
        Assert.Equal(30, april.DailyBalances.Count);
        Assert.Equal(1000, april.DailyBalances.Single(x => x.Day == 4).Saldo);
        Assert.Equal(2000, april.DailyBalances.Single(x => x.Day == 5).Saldo);
        Assert.Equal(1700, april.DailyBalances.Single(x => x.Day == 10).Saldo);
        Assert.Equal(april.Totals.Saldo, april.DailyBalances[^1].Saldo);
    }

    [Fact]
    public async Task Fixed_rule_change_recomputes_current_projection_window_when_new_start_is_future()
    {
        var harness = Harness();
        harness.Users.Seed("usr_gmarques", 1000);
        var futureRule = FixedRule("fxr_salary", "usr_gmarques", "income", 2000, "2026-07-05");
        harness.FixedRules.Seed(futureRule);

        await harness.Service.RecomputeForFixedRuleChangeAsync(futureRule, CancellationToken.None);

        var may = harness.Aggregates.Stored("usr_gmarques", new YearMonth(2026, 5))!;
        Assert.NotNull(may);
        Assert.Equal(1000, may.Totals.Saldo);
        Assert.False(may.Projection.IncludesProjected);
    }

    [Fact]
    public async Task Reprocessing_same_change_is_deterministic()
    {
        var harness = Harness();
        harness.Transactions.Seed(Tx("tx_income", "usr_gmarques", "2026-04", "2026-04-05", "income", "in", 1000));

        await harness.Service.RecomputeForChangeAsync("usr_gmarques", new YearMonth(2026, 4), CancellationToken.None);
        var first = harness.Aggregates.Stored("usr_gmarques", new YearMonth(2026, 4))!;
        await harness.Service.RecomputeForChangeAsync("usr_gmarques", new YearMonth(2026, 4), CancellationToken.None);
        var second = harness.Aggregates.Stored("usr_gmarques", new YearMonth(2026, 4))!;

        Assert.Equal(first.Totals.Entradas, second.Totals.Entradas);
        Assert.Equal(first.TransactionsCount, second.TransactionsCount);
        Assert.Equal(6, harness.Aggregates.UpsertCount);
    }

    private static RecomputeHarness Harness(DateTimeOffset? now = null)
    {
        var aggregates = new InMemoryMonthlyAggregatesRepository();
        var transactions = new InMemoryTransactionsProjectionRepository();
        var partnerships = new InMemoryPartnershipsReadRepository();
        var clock = new TestClock(now ?? DateTimeOffset.Parse("2026-04-25T15:00:00Z"));
        var options = new AggregatesOptions
        {
            SourceVersion = 4,
            BusinessTimeZone = "America/Sao_Paulo",
            MaxRebuildMonthsPerChange = 36,
            ProjectionMonthsAhead = 2
        };
        var users = new InMemoryUsersReadRepository();
        var fixedRules = new InMemoryFixedRulesProjectionRepository();
        var cards = new InMemoryCardsProjectionRepository();
        users.Seed("usr_gmarques", 0);
        users.Seed("usr_bmarques", 0);
        var calculator = new AggregateCalculator(options);
        var planner = new AggregateRebuildPlanner(aggregates, clock, options);
        var projectionService = new FixedRuleProjectionService(fixedRules, cards);
        var service = new AggregateRecomputeService(aggregates, transactions, partnerships, users, projectionService, calculator, planner, clock, options);
        return new RecomputeHarness(aggregates, transactions, partnerships, users, fixedRules, cards, service);
    }

    private static TransactionProjection Tx(
        string id,
        string userId,
        string yearMonth,
        string date,
        string category,
        string kind,
        decimal amount,
        bool deleted = false) =>
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
            CardId = category == "credit_card" ? "card_test" : null,
            FixedRuleId = null,
            DeletedAt = deleted ? DateTimeOffset.UtcNow : null
        };

    private static PartnershipDocument Partnership(string userId, string partnerUserId) =>
        new()
        {
            Id = $"pair_{userId}_{partnerUserId}_{userId}",
            UserId = userId,
            PartnerUserId = partnerUserId,
            Status = "active",
            MergedSince = new DateOnly(2026, 1, 12)
        };

    private static FixedRuleDocument FixedRule(
        string id,
        string userId,
        string category,
        decimal amount,
        string startsAt,
        string? cardId = null) =>
        new()
        {
            Id = id,
            DocType = "fixedRule",
            UserId = userId,
            Category = category,
            Description = "Fixed rule",
            Amount = amount,
            CardId = cardId,
            StartsAt = startsAt,
            Active = true,
            Schedule = new FixedRuleScheduleDocument
            {
                Type = "calendar_day",
                Day = DateOnly.Parse(startsAt).Day
            }
        };

    private sealed record RecomputeHarness(
        InMemoryMonthlyAggregatesRepository Aggregates,
        InMemoryTransactionsProjectionRepository Transactions,
        InMemoryPartnershipsReadRepository Partnerships,
        InMemoryUsersReadRepository Users,
        InMemoryFixedRulesProjectionRepository FixedRules,
        InMemoryCardsProjectionRepository Cards,
        AggregateRecomputeService Service);
}
