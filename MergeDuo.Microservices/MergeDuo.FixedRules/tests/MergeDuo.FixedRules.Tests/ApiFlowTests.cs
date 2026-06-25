using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MergeDuo.FixedRules.Domain.Contracts;
using MergeDuo.FixedRules.Domain.Documents;

namespace MergeDuo.FixedRules.Tests;

public sealed class ApiFlowTests
{
    [Fact]
    public async Task Protected_endpoints_reject_missing_jwt()
    {
        await using var factory = new TestFixedRulesFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/fixed-rules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Create_list_and_get_fixed_rule_for_authenticated_user()
    {
        await using var factory = new TestFixedRulesFactory();
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var create = await client.PostAsJsonAsync("/fixed-rules", new
        {
            category = "fixed_expense",
            description = "  Aluguel  ",
            amount = 2200m,
            tags = new[] { "Casa", " casa ", "Moradia" },
            schedule = new { type = "calendar_day", day = 5 },
            startsAt = "2026-04-01"
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var createdDoc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var created = createdDoc.RootElement;
        Assert.Equal("fxr_test_01", created.GetProperty("id").GetString());
        Assert.Equal("Aluguel", created.GetProperty("description").GetString());
        Assert.Equal(2200m, created.GetProperty("amount").GetDecimal());
        Assert.Equal(["casa", "moradia"], created.GetProperty("tags").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.False(created.TryGetProperty("userId", out _));
        Assert.False(created.TryGetProperty("_etag", out _));
        Assert.False(string.IsNullOrWhiteSpace(created.GetProperty("etag").GetString()));

        var list = await client.GetFromJsonAsync<FixedRulesListResponse>("/fixed-rules");
        Assert.NotNull(list);
        Assert.Single(list!.Items);
        Assert.Equal("fxr_test_01", list.Items[0].Id);
        Assert.False(string.IsNullOrWhiteSpace(list.Items[0].ETag));

        var get = await client.GetFromJsonAsync<FixedRuleResponse>("/fixed-rules/fxr_test_01");
        Assert.NotNull(get);
        Assert.Equal("Aluguel", get!.Description);
        Assert.Equal(["casa", "moradia"], get.Tags);
        Assert.False(string.IsNullOrWhiteSpace(get.ETag));
    }

    [Fact]
    public async Task Create_rejects_zero_amount_and_unknown_fields()
    {
        await using var factory = new TestFixedRulesFactory();
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var zero = await client.PostAsJsonAsync("/fixed-rules", new
        {
            category = "income",
            description = "Salario",
            amount = 0,
            schedule = new { type = "business_day", ordinal = 1 },
            startsAt = "2026-04-01"
        });
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal("invalid_amount", await ProblemCodeAsync(zero));

        var unknown = await client.PostAsJsonAsync("/fixed-rules", new
        {
            category = "income",
            description = "Salario",
            amount = 1000,
            schedule = new { type = "business_day", ordinal = 1 },
            startsAt = "2026-04-01",
            notes = "unsupported"
        });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("invalid_request", await ProblemCodeAsync(unknown));
    }

    [Fact]
    public async Task Create_inactive_rule_returns_structured_warning()
    {
        await using var factory = new TestFixedRulesFactory();
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var create = await client.PostAsJsonAsync("/fixed-rules", new
        {
            category = "fixed_expense",
            description = "Assinatura",
            amount = 39.9m,
            schedule = new { type = "calendar_day", day = 5 },
            startsAt = "2026-04-01",
            active = false
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var warning = doc.RootElement.GetProperty("warnings").EnumerateArray().Single();
        Assert.Equal("fixed_rule_inactive", warning.GetProperty("code").GetString());
        Assert.Equal("info", warning.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Credit_card_rules_require_existing_card_for_same_user()
    {
        await using var factory = new TestFixedRulesFactory();
        factory.Cards.Seed(new CardProjection { Id = "card_nubank_01", UserId = "usr_gmarques" });
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var missingCardId = await client.PostAsJsonAsync("/fixed-rules", new
        {
            category = "credit_card",
            description = "Streaming",
            amount = 39.9m,
            schedule = new { type = "period", period = "middle" },
            startsAt = "2026-04-01"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingCardId.StatusCode);
        Assert.Equal("invalid_card_id", await ProblemCodeAsync(missingCardId));

        var create = await client.PostAsJsonAsync("/fixed-rules", new
        {
            category = "credit_card",
            description = "Streaming",
            amount = 39.9m,
            cardId = "card_nubank_01",
            schedule = new { type = "period", period = "middle" },
            startsAt = "2026-04-01"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }

    [Fact]
    public async Task Fixed_rules_are_isolated_by_user_and_filtered_by_active()
    {
        await using var factory = new TestFixedRulesFactory();
        factory.FixedRules.Seed(Rule("usr_gmarques", "fxr_active", active: true));
        factory.FixedRules.Seed(Rule("usr_gmarques", "fxr_paused", active: false));
        factory.FixedRules.Seed(Rule("usr_other", "fxr_other", active: true));
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var active = await client.GetFromJsonAsync<FixedRulesListResponse>("/fixed-rules");
        Assert.NotNull(active);
        Assert.Single(active!.Items);
        Assert.Equal("fxr_active", active.Items[0].Id);

        var paused = await client.GetFromJsonAsync<FixedRulesListResponse>("/fixed-rules?active=false");
        Assert.NotNull(paused);
        Assert.Single(paused!.Items);
        Assert.Equal("fxr_paused", paused.Items[0].Id);

        var all = await client.GetFromJsonAsync<FixedRulesListResponse>("/fixed-rules?active=all");
        Assert.NotNull(all);
        Assert.Equal(2, all!.Items.Count);

        var other = await client.GetAsync("/fixed-rules/fxr_other");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal("fixed_rule_not_found", await ProblemCodeAsync(other));
    }

    [Fact]
    public async Task Patch_rejects_unknown_fields_and_honors_if_match()
    {
        await using var factory = new TestFixedRulesFactory();
        var rule = Rule("usr_gmarques", "fxr_rent");
        rule.ETag = "etag-1";
        factory.FixedRules.Seed(rule);
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var unknown = await client.PatchAsJsonAsync("/fixed-rules/fxr_rent", new { notes = "unsupported" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("invalid_request", await ProblemCodeAsync(unknown));

        var missingIfMatch = await client.PatchAsJsonAsync("/fixed-rules/fxr_rent", new { description = "Aluguel reajustado" });
        Assert.Equal(HttpStatusCode.PreconditionFailed, missingIfMatch.StatusCode);
        Assert.Equal("if_match_required", await ProblemCodeAsync(missingIfMatch));

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/fixed-rules/fxr_rent")
        {
            Content = JsonContent.Create(new { description = "Aluguel reajustado" })
        };
        request.Headers.TryAddWithoutValidation("If-Match", "wrong-etag");
        var stale = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("precondition_failed", await ProblemCodeAsync(stale));
    }

    [Fact]
    public async Task Create_patch_clear_and_validate_ends_at()
    {
        await using var factory = new TestFixedRulesFactory();
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var create = await client.PostAsJsonAsync("/fixed-rules", new
        {
            category = "fixed_expense",
            description = "Aluguel",
            amount = 2200m,
            schedule = new { type = "calendar_day", day = 5 },
            startsAt = "2026-04-01",
            endsAt = "2026-12-31"
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var createdDoc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        Assert.Equal("2026-12-31", createdDoc.RootElement.GetProperty("endsAt").GetString());
        Assert.Equal("2026-12-31", factory.FixedRules.Stored("usr_gmarques", "fxr_test_01")!.EndsAt);

        var patch = await PatchJsonAsync(client, "/fixed-rules/fxr_test_01", new { endsAt = "2026-11-30" }, ETag(factory, "fxr_test_01"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal("2026-11-30", factory.FixedRules.Stored("usr_gmarques", "fxr_test_01")!.EndsAt);

        var clear = await PatchJsonAsync(client, "/fixed-rules/fxr_test_01", new { endsAt = (string?)null }, ETag(factory, "fxr_test_01"));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Null(factory.FixedRules.Stored("usr_gmarques", "fxr_test_01")!.EndsAt);

        var invalidCreate = await client.PostAsJsonAsync("/fixed-rules", new
        {
            category = "fixed_expense",
            description = "Condominio",
            amount = 850m,
            schedule = new { type = "calendar_day", day = 10 },
            startsAt = "2026-04-01",
            endsAt = "2026-03-31"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);
        Assert.Equal("invalid_date_range", await ProblemCodeAsync(invalidCreate));

        var invalidPatch = await client.PatchAsJsonAsync("/fixed-rules/fxr_test_01", new { endsAt = "2026-03-31" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPatch.StatusCode);
        Assert.Equal("invalid_date_range", await ProblemCodeAsync(invalidPatch));
    }

    [Fact]
    public async Task Patch_updates_clears_and_previews_tags()
    {
        await using var factory = new TestFixedRulesFactory();
        var rule = Rule("usr_gmarques", "fxr_rent");
        rule.Tags = ["casa"];
        factory.FixedRules.Seed(rule);
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var update = await PatchJsonAsync(client, "/fixed-rules/fxr_rent", new
        {
            tags = new[] { "Moradia", "moradia", "Casa" }
        }, ETag(factory, "fxr_rent"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(["moradia", "casa"], factory.FixedRules.Stored("usr_gmarques", "fxr_rent")!.Tags);

        var preview = await client.GetFromJsonAsync<FixedRulePreviewResponse>("/fixed-rules/fxr_rent/preview?from=2026-04-01&to=2026-04-30");
        Assert.NotNull(preview);
        Assert.Equal(["moradia", "casa"], preview!.Items.Single().Tags);

        var clear = await PatchJsonAsync(client, "/fixed-rules/fxr_rent", new { tags = Array.Empty<string>() }, ETag(factory, "fxr_rent"));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Empty(factory.FixedRules.Stored("usr_gmarques", "fxr_rent")!.Tags);
    }

    [Fact]
    public async Task Pause_resume_and_delete_update_expected_fields()
    {
        await using var factory = new TestFixedRulesFactory();
        factory.FixedRules.Seed(Rule("usr_gmarques", "fxr_rent"));
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var pause = await PostWithIfMatchAsync(client, "/fixed-rules/fxr_rent/pause", ETag(factory, "fxr_rent"));
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        Assert.False(factory.FixedRules.Stored("usr_gmarques", "fxr_rent")!.Active);

        var resume = await PostWithIfMatchAsync(client, "/fixed-rules/fxr_rent/resume", ETag(factory, "fxr_rent"));
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        Assert.True(factory.FixedRules.Stored("usr_gmarques", "fxr_rent")!.Active);

        var delete = await DeleteWithIfMatchAsync(client, "/fixed-rules/fxr_rent", ETag(factory, "fxr_rent"));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var stored = factory.FixedRules.Stored("usr_gmarques", "fxr_rent")!;
        Assert.False(stored.Active);
        Assert.NotNull(stored.DeletedAt);

        var get = await client.GetAsync("/fixed-rules/fxr_rent");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Preview_returns_occurrences_for_paused_rule_without_persisting_transactions()
    {
        await using var factory = new TestFixedRulesFactory();
        factory.FixedRules.Seed(Rule(
            "usr_gmarques",
            "fxr_salary",
            active: false,
            category: "income",
            schedule: new FixedRuleScheduleDocument { Type = "business_day", Ordinal = 1 }));
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var response = await client.GetAsync("/fixed-rules/fxr_salary/preview?from=2026-05-01&to=2026-05-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        Assert.False(root.GetProperty("active").GetBoolean());
        Assert.Equal("2026-05-01", root.GetProperty("items")[0].GetProperty("occurrenceDate").GetString());
    }

    private static void Authorize(HttpClient client, TestFixedRulesFactory factory, string userId) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.IssueToken(userId));

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("code").GetString();
    }

    private static string ETag(TestFixedRulesFactory factory, string fixedRuleId) =>
        factory.FixedRules.Stored("usr_gmarques", fixedRuleId)!.ETag!;

    private static async Task<HttpResponseMessage> PatchJsonAsync(HttpClient client, string uri, object body, string etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, uri)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostWithIfMatchAsync(HttpClient client, string uri, string etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteWithIfMatchAsync(HttpClient client, string uri, string etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request);
    }

    private static FixedRuleDocument Rule(
        string userId,
        string id,
        bool active = true,
        string category = "fixed_expense",
        FixedRuleScheduleDocument? schedule = null) =>
        new()
        {
            Id = id,
            DocType = "fixedRule",
            SchemaVersion = 1,
            UserId = userId,
            Category = category,
            Description = category == "income" ? "Salario" : "Aluguel",
            Amount = category == "income" ? 8500m : 2200m,
            CardId = null,
            Tags = [],
            Schedule = schedule ?? new FixedRuleScheduleDocument { Type = "calendar_day", Day = 5 },
            StartsAt = "2026-04-01",
            EndsAt = null,
            Active = active,
            CreatedAt = DateTimeOffset.Parse("2026-04-01T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-04-01T12:00:00Z"),
            ETag = "etag-1"
        };
}
