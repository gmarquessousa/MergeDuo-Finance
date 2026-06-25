using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MergeDuo.Scheduler.Tests;

public sealed class SchedulerCoreTests
{
    [Fact]
    public void Build_transaction_request_maps_credit_card_occurrence_to_purchase_date()
    {
        var rule = Rule(category: global::Categories.CreditCard, cardId: "card_1");

        var request = global::SchedulerCore.BuildTransactionRequest(rule, new DateOnly(2026, 4, 29));

        Assert.Null(request.Date);
        Assert.Equal(new DateOnly(2026, 4, 29), request.PurchaseDate);
        Assert.Equal("card_1", request.CardId);
        Assert.Equal(["assinatura"], request.Tags);
        Assert.Equal("fxr_test", request.FixedRuleId);
    }

    [Fact]
    public void Build_transaction_request_maps_non_card_occurrence_to_cash_date()
    {
        var rule = Rule(category: global::Categories.FixedExpense);

        var request = global::SchedulerCore.BuildTransactionRequest(rule, new DateOnly(2026, 4, 29));

        Assert.Equal(new DateOnly(2026, 4, 29), request.Date);
        Assert.Null(request.PurchaseDate);
        Assert.Null(request.CardId);
        Assert.Equal(["assinatura"], request.Tags);
        Assert.Equal("fxr_test", request.FixedRuleId);
    }

    [Fact]
    public async Task Create_transaction_async_sends_internal_key_and_idempotency_key()
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new
            {
                groupId = (string?)null,
                items = new[] { new { id = "tx_1", yearMonth = "2026-04" } }
            })
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://transactions.local/") };
        var options = new global::TransactionsServiceOptions { InternalKey = "scheduler-key" };

        var response = await global::SchedulerCore.CreateTransactionAsync(
            client,
            options,
            Rule(category: global::Categories.FixedExpense),
            new DateOnly(2026, 4, 29),
            CancellationToken.None);

        Assert.Equal("tx_1", response.Items.Single().Id);
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://transactions.local/internal/scheduler/transactions", handler.Request.RequestUri!.ToString());
        Assert.Equal("scheduler-key", handler.Request.Headers.GetValues("X-MergeDuo-Internal-Key").Single());
        Assert.Equal("fixed-rule:fxr_test:2026-04-29", handler.Request.Headers.GetValues("Idempotency-Key").Single());

        var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("usr_1", body.RootElement.GetProperty("userId").GetString());
        var transaction = body.RootElement.GetProperty("transaction");
        Assert.Equal("2026-04-29", transaction.GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.Null, transaction.GetProperty("purchaseDate").ValueKind);
        Assert.Equal(["assinatura"], transaction.GetProperty("tags").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal("fxr_test", transaction.GetProperty("fixedRuleId").GetString());
    }

    [Fact]
    public async Task Create_transaction_async_throws_when_transactions_service_fails()
    {
        var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream failure")
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://transactions.local/") };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            global::SchedulerCore.CreateTransactionAsync(
                client,
                new global::TransactionsServiceOptions { InternalKey = "scheduler-key" },
                Rule(category: global::Categories.FixedExpense),
                new DateOnly(2026, 4, 29),
                CancellationToken.None));

        Assert.Contains("502", exception.Message);
        Assert.Contains("upstream failure", exception.Message);
    }

    [Fact]
    public async Task Process_due_rule_updates_checkpoint_only_after_transaction_success()
    {
        var checkpointCalls = 0;

        await global::SchedulerCore.ProcessDueRuleAsync(
            Rule(category: global::Categories.FixedExpense),
            new DateOnly(2026, 4, 29),
            _ => Task.CompletedTask,
            (_, _) =>
            {
                checkpointCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(1, checkpointCalls);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            global::SchedulerCore.ProcessDueRuleAsync(
                Rule(category: global::Categories.FixedExpense),
                new DateOnly(2026, 4, 29),
                _ => throw new InvalidOperationException("http failure"),
                (_, _) =>
                {
                    checkpointCalls++;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

        Assert.Equal(1, checkpointCalls);
    }

    [Fact]
    public void Is_due_today_respects_next_run_checkpoint()
    {
        var today = new DateOnly(2026, 4, 29);
        var alreadyCheckpointed = Rule(category: global::Categories.FixedExpense);
        alreadyCheckpointed.NextRunAt = "2026-05-29";

        var due = Rule(category: global::Categories.FixedExpense);
        due.NextRunAt = "2026-04-29";

        Assert.False(global::SchedulerCore.IsDueToday(alreadyCheckpointed, today));
        Assert.True(global::SchedulerCore.IsDueToday(due, today));
    }

    [Fact]
    public void Resolve_due_occurrence_backfills_current_month_occurrence_without_checkpoint()
    {
        var rule = Rule(category: global::Categories.FixedExpense);
        rule.StartsAt = "2026-05-01";
        rule.Schedule = new global::FixedRuleScheduleDocument
        {
            Type = "business_day",
            Ordinal = 1
        };

        var dueOccurrence = global::SchedulerCore.ResolveDueOccurrence(rule, new DateOnly(2026, 5, 7));

        Assert.Equal(new DateOnly(2026, 5, 1), dueOccurrence);
    }

    [Fact]
    public void Resolve_due_occurrence_uses_overdue_next_run_checkpoint()
    {
        var rule = Rule(category: global::Categories.FixedExpense);
        rule.NextRunAt = "2026-05-01";

        var dueOccurrence = global::SchedulerCore.ResolveDueOccurrence(rule, new DateOnly(2026, 5, 7));

        Assert.Equal(new DateOnly(2026, 5, 1), dueOccurrence);
    }

    [Fact]
    public void Resolve_due_occurrence_skips_current_month_when_last_run_already_covered_it()
    {
        var rule = Rule(category: global::Categories.FixedExpense);
        rule.StartsAt = "2026-05-01";
        rule.Schedule = new global::FixedRuleScheduleDocument
        {
            Type = "business_day",
            Ordinal = 1
        };
        rule.LastRunAt = "2026-05-01";

        var dueOccurrence = global::SchedulerCore.ResolveDueOccurrence(rule, new DateOnly(2026, 5, 7));

        Assert.Null(dueOccurrence);
    }

    [Fact]
    public async Task Process_due_rule_skips_occurrence_after_ends_at()
    {
        var createCalls = 0;
        var checkpointCalls = 0;
        var rule = Rule(category: global::Categories.FixedExpense);
        rule.EndsAt = "2026-04-28";

        var processed = await global::SchedulerCore.ProcessDueRuleAsync(
            rule,
            new DateOnly(2026, 4, 29),
            _ =>
            {
                createCalls++;
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                checkpointCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(processed);
        Assert.Equal(0, createCalls);
        Assert.Equal(0, checkpointCalls);
    }

    [Fact]
    public async Task Process_due_rule_accepts_occurrence_on_ends_at()
    {
        var createCalls = 0;
        var checkpointCalls = 0;
        var rule = Rule(category: global::Categories.FixedExpense);
        rule.EndsAt = "2026-04-29";

        var processed = await global::SchedulerCore.ProcessDueRuleAsync(
            rule,
            new DateOnly(2026, 4, 29),
            _ =>
            {
                createCalls++;
                return Task.CompletedTask;
            },
            (nextRunAt, _) =>
            {
                checkpointCalls++;
                Assert.Null(nextRunAt);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(1, createCalls);
        Assert.Equal(1, checkpointCalls);
    }

    [Fact]
    public void Next_occurrence_returns_null_when_window_has_ended()
    {
        var rule = Rule(category: global::Categories.FixedExpense);
        rule.EndsAt = "2026-04-29";

        var next = global::SchedulerCore.NextOccurrence(rule, new DateOnly(2026, 4, 29));

        Assert.Null(next);
    }

    private static global::FixedRuleDocument Rule(string category, string? cardId = null) =>
        new()
        {
            Id = "fxr_test",
            UserId = "usr_1",
            Category = category,
            Description = "Recurring",
            Amount = 123.45m,
            CardId = cardId,
            Tags = ["assinatura"],
            StartsAt = "2026-04-01",
            Schedule = new global::FixedRuleScheduleDocument
            {
                Type = "calendar_day",
                Day = 29
            }
        };

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}
