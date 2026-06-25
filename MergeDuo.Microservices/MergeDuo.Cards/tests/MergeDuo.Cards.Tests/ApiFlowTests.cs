using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MergeDuo.Cards.Domain.Contracts;
using MergeDuo.Cards.Domain.Documents;

namespace MergeDuo.Cards.Tests;

public sealed class ApiFlowTests
{
    [Fact]
    public async Task Protected_endpoints_reject_missing_jwt()
    {
        await using var factory = new TestCardsFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/cards");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Create_list_and_get_card_for_authenticated_user()
    {
        await using var factory = new TestCardsFactory();
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var create = await client.PostAsJsonAsync("/cards", new
        {
            title = "  Nubank Roxinho  ",
            closingDay = 28,
            dueDay = 5,
            currency = "brl"
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var createdDoc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var created = createdDoc.RootElement;
        Assert.Equal("card_test_01", created.GetProperty("id").GetString());
        Assert.Equal("Nubank Roxinho", created.GetProperty("title").GetString());
        Assert.Equal("BRL", created.GetProperty("currency").GetString());
        Assert.False(created.TryGetProperty("userId", out _));
        Assert.False(created.TryGetProperty("_etag", out _));
        Assert.False(string.IsNullOrWhiteSpace(created.GetProperty("etag").GetString()));

        var list = await client.GetFromJsonAsync<CardsListResponse>("/cards");
        Assert.NotNull(list);
        Assert.Single(list!.Items);
        Assert.Equal("card_test_01", list.Items[0].Id);
        Assert.False(string.IsNullOrWhiteSpace(list.Items[0].ETag));

        var get = await client.GetFromJsonAsync<CardResponse>("/cards/card_test_01");
        Assert.NotNull(get);
        Assert.Equal("Nubank Roxinho", get!.Title);
        Assert.False(string.IsNullOrWhiteSpace(get.ETag));
    }

    [Fact]
    public async Task Cards_are_isolated_by_user()
    {
        await using var factory = new TestCardsFactory();
        factory.Cards.Seed(Card("usr_gmarques", "card_nubank_01"));
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_other");

        var response = await client.GetAsync("/cards/card_nubank_01");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("card_not_found", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Create_rejects_invalid_billing_day()
    {
        await using var factory = new TestCardsFactory();
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var response = await client.PostAsJsonAsync("/cards", new
        {
            title = "Nubank",
            closingDay = 0,
            dueDay = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_billing_day", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Patch_rejects_unknown_fields_and_honors_if_match()
    {
        await using var factory = new TestCardsFactory();
        var card = Card("usr_gmarques", "card_nubank_01");
        card.ETag = "etag-1";
        factory.Cards.Seed(card);
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var unknown = await client.PatchAsJsonAsync("/cards/card_nubank_01", new { limit = 1000 });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("invalid_request", await ProblemCodeAsync(unknown));

        var missingIfMatch = await client.PatchAsJsonAsync("/cards/card_nubank_01", new { title = "Nubank Ultravioleta" });
        Assert.Equal(HttpStatusCode.PreconditionFailed, missingIfMatch.StatusCode);
        Assert.Equal("if_match_required", await ProblemCodeAsync(missingIfMatch));

        using var request = new HttpRequestMessage(HttpMethod.Patch, "/cards/card_nubank_01")
        {
            Content = JsonContent.Create(new { title = "Nubank Ultravioleta" })
        };
        request.Headers.TryAddWithoutValidation("If-Match", "wrong-etag");
        var stale = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("precondition_failed", await ProblemCodeAsync(stale));
    }

    [Fact]
    public async Task Delete_soft_deletes_card_and_hides_it()
    {
        await using var factory = new TestCardsFactory();
        factory.Cards.Seed(Card("usr_gmarques", "card_nubank_01"));
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/cards/card_nubank_01");
        request.Headers.TryAddWithoutValidation("If-Match", "etag-1");
        var delete = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.NotNull(factory.Cards.Stored("usr_gmarques", "card_nubank_01")!.DeletedAt);

        var get = await client.GetAsync("/cards/card_nubank_01");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Usage_validates_card_and_returns_totals_with_billing_cycle()
    {
        await using var factory = new TestCardsFactory();
        factory.Cards.Seed(Card("usr_gmarques", "card_nubank_01", closingDay: 28, dueDay: 5));
        factory.Usage.Seed(
            "usr_gmarques",
            new CardUsageTotals("card_nubank_01", "2026-05", "BRL", 253.33m, 1, 1));
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var response = await client.GetAsync("/cards/card_nubank_01/usage?ym=2026-05");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        Assert.Equal("card_nubank_01", root.GetProperty("cardId").GetString());
        Assert.Equal(253.33m, root.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("2026-04-28", root.GetProperty("billingCycle").GetProperty("closingDate").GetString());
        Assert.Equal("2026-05-05", root.GetProperty("billingCycle").GetProperty("dueDate").GetString());
        Assert.Equal("transactions-service", root.GetProperty("freshness").GetProperty("source").GetString());
        Assert.Equal("fresh", root.GetProperty("freshness").GetProperty("status").GetString());
        Assert.True(root.GetProperty("freshness").GetProperty("isFresh").GetBoolean());
        Assert.False(root.GetProperty("freshness").GetProperty("isFallback").GetBoolean());
        Assert.Single(factory.Usage.Calls);
    }

    [Fact]
    public async Task Usage_returns_503_when_transactions_dependency_fails()
    {
        await using var factory = new TestCardsFactory();
        factory.Cards.Seed(Card("usr_gmarques", "card_nubank_01"));
        factory.Usage.Fail = true;
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var response = await client.GetAsync("/cards/card_nubank_01/usage?ym=2026-05");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("cards_dependency_unavailable", await ProblemCodeAsync(response));
    }

    private static void Authorize(HttpClient client, TestCardsFactory factory, string userId) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.IssueToken(userId));

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("code").GetString();
    }

    private static CardDocument Card(
        string userId,
        string id,
        int closingDay = 28,
        int dueDay = 5) =>
        new()
        {
            Id = id,
            DocType = "card",
            SchemaVersion = 1,
            UserId = userId,
            Title = "Nubank Roxinho",
            ClosingDay = closingDay,
            DueDay = dueDay,
            Currency = "BRL",
            CreatedAt = DateTimeOffset.Parse("2024-03-04T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2024-03-04T12:00:00Z"),
            DeletedAt = null,
            ETag = "etag-1"
        };
}
