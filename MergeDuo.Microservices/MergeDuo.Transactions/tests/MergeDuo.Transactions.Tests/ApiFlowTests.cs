using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MergeDuo.Transactions.Domain.Contracts;
using MergeDuo.Transactions.Domain.Documents;

namespace MergeDuo.Transactions.Tests;

public sealed class ApiFlowTests
{
    [Fact]
    public async Task Protected_endpoints_reject_missing_jwt()
    {
        await using var factory = new TestTransactionsFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/transactions?ym=2026-05");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Create_list_and_get_variable_expense()
    {
        await using var factory = new TestTransactionsFactory();
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var create = await client.PostAsJsonAsync("/transactions", new
        {
            date = "2026-04-28",
            category = "variable_expense",
            description = " Mercado ",
            amount = 120.50m,
            currency = "brl",
            tags = new[] { "Casa" }
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CreateTransactionsResponse>();
        Assert.NotNull(created);
        Assert.Null(created!.GroupId);
        Assert.Single(created.Items);
        Assert.Equal("tx_test_01", created.Items[0].Id);
        Assert.Equal("2026-04", created.Items[0].YearMonth);
        Assert.Equal("out", created.Items[0].Kind);
        Assert.Equal(["casa"], created.Items[0].Tags);
        Assert.False(string.IsNullOrWhiteSpace(created.Items[0].ETag));

        var list = await client.GetFromJsonAsync<TransactionsListResponse>("/transactions?ym=2026-04");
        Assert.NotNull(list);
        Assert.Single(list!.Items);
        Assert.False(string.IsNullOrWhiteSpace(list.Items[0].ETag));

        var get = await client.GetFromJsonAsync<TransactionResponse>("/transactions/tx_test_01?ym=2026-04");
        Assert.NotNull(get);
        Assert.Equal("Mercado", get!.Description);
        Assert.Equal(["casa"], get.Tags);
        Assert.False(string.IsNullOrWhiteSpace(get.ETag));
    }

    [Fact]
    public async Task Credit_card_rejects_date_and_creates_installments()
    {
        await using var factory = new TestTransactionsFactory();
        factory.Aux.Seed(Card("usr_gmarques", "card_nubank_01"));
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var invalid = await client.PostAsJsonAsync("/transactions", new
        {
            date = "2026-05-05",
            purchaseDate = "2026-04-10",
            category = "credit_card",
            description = "Compra",
            amount = 100m,
            cardId = "card_nubank_01"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_date", await ProblemCodeAsync(invalid));

        var createdResponse = await client.PostAsJsonAsync("/transactions", new
        {
            purchaseDate = "2026-04-10",
            category = "credit_card",
            description = "Eletrodomestico",
            amount = 760m,
            cardId = "card_nubank_01",
            installments = new { total = 3 }
        });

        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<CreateTransactionsResponse>();
        Assert.NotNull(created);
        Assert.Equal("txg_test_01", created!.GroupId);
        Assert.Equal(3, created.Items.Count);
        Assert.Equal(["2026-05", "2026-06", "2026-07"], created.Items.Select(x => x.YearMonth).ToArray());
        Assert.Equal(760m, created.Items.Sum(x => x.Amount));
    }

    [Fact]
    public async Task Idempotency_key_retries_same_payload()
    {
        await using var factory = new TestTransactionsFactory();
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        using var first = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(new
            {
                date = "2026-04-28",
                category = "income",
                description = "Salario",
                amount = 1000m
            })
        };
        first.Headers.TryAddWithoutValidation("Idempotency-Key", "idem-1");
        var firstResponse = await client.SendAsync(first);

        using var retry = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(new
            {
                date = "2026-04-28",
                category = "income",
                description = "Salario",
                amount = 1000m
            })
        };
        retry.Headers.TryAddWithoutValidation("Idempotency-Key", "idem-1");
        var retryResponse = await client.SendAsync(retry);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retryResponse.StatusCode);
    }

    [Fact]
    public async Task Idempotency_key_rejects_reused_key_with_different_payload()
    {
        await using var factory = new TestTransactionsFactory();
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        using var first = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(new
            {
                date = "2026-04-28",
                category = "income",
                description = "Salario",
                amount = 1000m
            })
        };
        first.Headers.TryAddWithoutValidation("Idempotency-Key", "idem-reused");
        var firstResponse = await client.SendAsync(first);

        using var second = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(new
            {
                date = "2026-04-28",
                category = "income",
                description = "Salario",
                amount = 1001m
            })
        };
        second.Headers.TryAddWithoutValidation("Idempotency-Key", "idem-reused");
        var secondResponse = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal("idempotency_key_reused", await ProblemCodeAsync(secondResponse));
    }

    [Fact]
    public async Task Idempotency_key_rejects_reused_key_with_different_tags()
    {
        await using var factory = new TestTransactionsFactory();
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        using var first = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(new
            {
                date = "2026-04-28",
                category = "income",
                description = "Salario",
                amount = 1000m,
                tags = new[] { "salario" }
            })
        };
        first.Headers.TryAddWithoutValidation("Idempotency-Key", "idem-tags");
        var firstResponse = await client.SendAsync(first);

        using var second = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(new
            {
                date = "2026-04-28",
                category = "income",
                description = "Salario",
                amount = 1000m,
                tags = new[] { "bonus" }
            })
        };
        second.Headers.TryAddWithoutValidation("Idempotency-Key", "idem-tags");
        var secondResponse = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal("idempotency_key_reused", await ProblemCodeAsync(secondResponse));
    }

    [Fact]
    public async Task Internal_scheduler_endpoint_requires_key_and_accepts_fxr_fixed_rule()
    {
        await using var factory = new TestTransactionsFactory();
        factory.Aux.Seed(new FixedRuleDocument
        {
            Id = "fxr_salary",
            UserId = "usr_gmarques",
            Category = "income",
            Active = true
        });
        using var client = factory.CreateHttpsClient();

        var missingKey = await client.PostAsJsonAsync("/internal/scheduler/transactions", new
        {
            userId = "usr_gmarques",
            transaction = new
            {
                date = "2026-04-28",
                category = "income",
                description = "Salario",
                amount = 1000m,
                fixedRuleId = "fxr_salary"
            }
        });
        Assert.Equal(HttpStatusCode.Unauthorized, missingKey.StatusCode);

        using var create = new HttpRequestMessage(HttpMethod.Post, "/internal/scheduler/transactions")
        {
            Content = JsonContent.Create(new
            {
                userId = "usr_gmarques",
                transaction = new
                {
                    date = "2026-04-28",
                    category = "income",
                    description = "Salario",
                    amount = 1000m,
                    fixedRuleId = "fxr_salary"
                }
            })
        };
        create.Headers.TryAddWithoutValidation("X-MergeDuo-Internal-Key", "test-scheduler-key");
        create.Headers.TryAddWithoutValidation("Idempotency-Key", "fixed-rule:fxr_salary:2026-04-28");

        var response = await client.SendAsync(create);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreateTransactionsResponse>();
        Assert.NotNull(created);
        Assert.Equal("fxr_salary", created!.Items.Single().FixedRuleId);
        Assert.Equal("fixed_rule", created.Items.Single().Source.Channel);
    }

    [Fact]
    public async Task Partner_owner_filter_reads_active_partner()
    {
        await using var factory = new TestTransactionsFactory();
        factory.Aux.Seed(new PartnershipDocument
        {
            Id = "pair_1",
            UserId = "usr_gmarques",
            PartnerUserId = "usr_bmarques",
            Status = "active"
        });
        factory.Aux.Seed(Card("usr_bmarques", "card_itau_01", "Itau Duo"));
        factory.Transactions.Seed(Transaction("usr_bmarques", "tx_partner_01", "2026-04", category: "credit_card", cardId: "card_itau_01"));
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var response = await client.GetFromJsonAsync<TransactionsListResponse>("/transactions?ym=2026-04&owner=partner");

        Assert.NotNull(response);
        Assert.Single(response!.Items);
        Assert.Equal("usr_bmarques", response.Items[0].UserId);
        Assert.Equal("Itau Duo", response.Items[0].CardTitle);
    }

    [Fact]
    public async Task Both_owner_filter_only_includes_card_title_for_partner_cards()
    {
        await using var factory = new TestTransactionsFactory();
        factory.Aux.Seed(new PartnershipDocument
        {
            Id = "pair_1",
            UserId = "usr_gmarques",
            PartnerUserId = "usr_bmarques",
            Status = "active"
        });
        factory.Aux.Seed(Card("usr_gmarques", "card_own_01", "Nubank Proprio"));
        factory.Aux.Seed(Card("usr_bmarques", "card_partner_01", "Nubank Duo"));
        factory.Transactions.Seed(Transaction("usr_gmarques", "tx_own_card", "2026-04", category: "credit_card", cardId: "card_own_01"));
        factory.Transactions.Seed(Transaction("usr_bmarques", "tx_partner_card", "2026-04", category: "credit_card", cardId: "card_partner_01"));
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var response = await client.GetFromJsonAsync<TransactionsListResponse>("/transactions?ym=2026-04&owner=both");

        Assert.NotNull(response);
        var own = Assert.Single(response!.Items.Where(x => x.Id == "tx_own_card"));
        var partner = Assert.Single(response.Items.Where(x => x.Id == "tx_partner_card"));
        Assert.Null(own.CardTitle);
        Assert.Equal("Nubank Duo", partner.CardTitle);
    }

    [Fact]
    public async Task Get_partner_transaction_includes_card_title()
    {
        await using var factory = new TestTransactionsFactory();
        factory.Aux.Seed(new PartnershipDocument
        {
            Id = "pair_1",
            UserId = "usr_gmarques",
            PartnerUserId = "usr_bmarques",
            Status = "active"
        });
        factory.Aux.Seed(Card("usr_bmarques", "card_itau_01", "Itau Duo"));
        factory.Transactions.Seed(Transaction("usr_bmarques", "tx_partner_card", "2026-04", category: "credit_card", cardId: "card_itau_01"));
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var response = await client.GetFromJsonAsync<TransactionResponse>("/transactions/tx_partner_card?ym=2026-04&ownerUserId=usr_bmarques");

        Assert.NotNull(response);
        Assert.Equal("usr_bmarques", response!.UserId);
        Assert.Equal("Itau Duo", response.CardTitle);
    }

    [Fact]
    public async Task Get_partner_group_includes_card_title()
    {
        await using var factory = new TestTransactionsFactory();
        factory.Aux.Seed(new PartnershipDocument
        {
            Id = "pair_1",
            UserId = "usr_gmarques",
            PartnerUserId = "usr_bmarques",
            Status = "active"
        });
        factory.Aux.Seed(Card("usr_bmarques", "card_itau_01", "Itau Duo"));
        var first = Transaction("usr_bmarques", "tx_partner_card_01", "2026-04", category: "credit_card", cardId: "card_itau_01");
        first.Installments = new InstallmentDocument { Index = 1, Total = 2, GroupId = "txg_partner_card" };
        var second = Transaction("usr_bmarques", "tx_partner_card_02", "2026-05", category: "credit_card", cardId: "card_itau_01");
        second.Installments = new InstallmentDocument { Index = 2, Total = 2, GroupId = "txg_partner_card" };
        factory.Transactions.Seed(first);
        factory.Transactions.Seed(second);
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var response = await client.GetFromJsonAsync<TransactionGroupResponse>("/transactions/groups/txg_partner_card?ownerUserId=usr_bmarques");

        Assert.NotNull(response);
        Assert.Equal(2, response!.Items.Count);
        Assert.All(response.Items, item => Assert.Equal("Itau Duo", item.CardTitle));
    }

    [Fact]
    public async Task Patch_moves_transaction_between_months_and_delete_soft_deletes()
    {
        await using var factory = new TestTransactionsFactory();
        factory.Transactions.Seed(Transaction("usr_gmarques", "tx_existing_01", "2026-04", etag: "etag-1"));
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        using var patch = new HttpRequestMessage(HttpMethod.Patch, "/transactions/tx_existing_01?ym=2026-04")
        {
            Content = JsonContent.Create(new
            {
                date = "2026-05-01",
                description = "Mercado atualizado",
                tags = new[] { "Mercado", "Casa" }
            })
        };
        patch.Headers.TryAddWithoutValidation("If-Match", "etag-1");
        var patchResponse = await client.SendAsync(patch);

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        Assert.NotNull(factory.Transactions.Stored("usr_gmarques", "2026-05", "tx_existing_01"));
        Assert.NotNull(factory.Transactions.Stored("usr_gmarques", "2026-04", "tx_existing_01")!.DeletedAt);

        var moved = factory.Transactions.Stored("usr_gmarques", "2026-05", "tx_existing_01")!;
        Assert.Equal(["mercado", "casa"], moved.Tags);
    var missingIfMatch = await client.DeleteAsync($"/transactions/tx_existing_01?ym=2026-05");
    Assert.Equal(HttpStatusCode.PreconditionFailed, missingIfMatch.StatusCode);
    Assert.Equal("if_match_required", await ProblemCodeAsync(missingIfMatch));

    using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/transactions/tx_existing_01?ym=2026-05");
    deleteRequest.Headers.TryAddWithoutValidation("If-Match", moved.ETag);
    var delete = await client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.NotNull(factory.Transactions.Stored("usr_gmarques", "2026-05", moved.Id)!.DeletedAt);
    }

    [Fact]
    public async Task Card_usage_returns_totals_for_authenticated_user()
    {
        await using var factory = new TestTransactionsFactory();
        factory.Aux.Seed(Card("usr_gmarques", "card_nubank_01"));
        factory.Transactions.Seed(Transaction("usr_gmarques", "tx_card_01", "2026-05", category: "credit_card", cardId: "card_nubank_01", amount: 253.33m));
        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var response = await client.GetFromJsonAsync<CardUsageResponse>("/internal/transactions/card-usage?cardId=card_nubank_01&ym=2026-05");

        Assert.NotNull(response);
        Assert.Equal(253.33m, response!.TotalAmount);
        Assert.Equal(1, response.TransactionCount);
    }

    [Fact]
    public async Task Tags_endpoint_aggregates_current_user_active_partner_and_fixed_rule_tags()
    {
        await using var factory = new TestTransactionsFactory();
        factory.Aux.Seed(new PartnershipDocument
        {
            Id = "pair_1",
            UserId = "usr_gmarques",
            PartnerUserId = "usr_bmarques",
            Status = "active"
        });
        factory.Aux.Seed(new FixedRuleDocument
        {
            Id = "fxr_subscription",
            UserId = "usr_gmarques",
            Category = "fixed_expense",
            Active = true,
            Tags = ["assinatura"]
        });
        factory.Aux.Seed(new FixedRuleDocument
        {
            Id = "fxr_partner",
            UserId = "usr_bmarques",
            Category = "fixed_expense",
            Active = true,
            Tags = ["parceiro-regra"]
        });
        factory.Aux.Seed(Card("usr_bmarques", "card_itau_01", "Itau Duo"));

        var ownExpense = Transaction("usr_gmarques", "tx_own_market", "2026-04", amount: 120m);
        ownExpense.Tags = ["Mercado", "Casa", "mercado"];
        var partnerExpense = Transaction("usr_bmarques", "tx_partner_market", "2026-03", category: "credit_card", cardId: "card_itau_01", amount: 80m);
        partnerExpense.Tags = ["mercado"];
        var ownIncome = Transaction("usr_gmarques", "tx_own_income", "2026-04", category: "income", amount: 1000m);
        ownIncome.Tags = ["Renda"];
        var deletedExpense = Transaction("usr_gmarques", "tx_deleted_market", "2026-04", amount: 999m);
        deletedExpense.Tags = ["mercado"];
        deletedExpense.DeletedAt = DateTimeOffset.Parse("2026-04-30T12:00:00Z");

        factory.Transactions.Seed(ownExpense);
        factory.Transactions.Seed(partnerExpense);
        factory.Transactions.Seed(ownIncome);
        factory.Transactions.Seed(deletedExpense);

        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var response = await client.GetFromJsonAsync<TagAnalyticsResponse>("/transactions/tags?includeTransactions=true");

        Assert.NotNull(response);
        Assert.Contains("mercado", response!.Tags);
        Assert.Contains("assinatura", response.Tags);
        Assert.Contains("parceiro-regra", response.Tags);

        var mercado = Summary(response, "mercado");
        Assert.Equal(200m, mercado.ExpensesTotal);
        Assert.Equal(2, mercado.TransactionCount);
        Assert.Equal(["tx_own_market", "tx_partner_market"], mercado.Transactions!.Select(x => x.Id).ToArray());
        Assert.Null(mercado.Transactions!.Single(x => x.Id == "tx_own_market").CardTitle);
        Assert.Equal("Itau Duo", mercado.Transactions!.Single(x => x.Id == "tx_partner_market").CardTitle);

        var casa = Summary(response, "casa");
        Assert.Equal(120m, casa.ExpensesTotal);
        Assert.Equal(1, casa.TransactionCount);

        var renda = Summary(response, "renda");
        Assert.Equal(0m, renda.ExpensesTotal);
        Assert.Equal(1, renda.TransactionCount);
        Assert.Equal("income", renda.Transactions!.Single().Category);

        var assinatura = Summary(response, "assinatura");
        Assert.Equal(0m, assinatura.ExpensesTotal);
        Assert.Equal(0, assinatura.TransactionCount);
        Assert.Empty(assinatura.Transactions!);
        Assert.DoesNotContain(response.Items.SelectMany(x => x.Transactions ?? Array.Empty<TransactionResponse>()), x => x.Id == "tx_deleted_market");
    }

    [Fact]
    public async Task Tags_endpoint_does_not_include_partner_without_active_merge()
    {
        await using var factory = new TestTransactionsFactory();
        var ownExpense = Transaction("usr_gmarques", "tx_own_house", "2026-04", amount: 120m);
        ownExpense.Tags = ["casa"];
        var partnerExpense = Transaction("usr_bmarques", "tx_partner_market", "2026-04", amount: 80m);
        partnerExpense.Tags = ["mercado"];
        factory.Transactions.Seed(ownExpense);
        factory.Transactions.Seed(partnerExpense);
        factory.Aux.Seed(new FixedRuleDocument
        {
            Id = "fxr_partner",
            UserId = "usr_bmarques",
            Category = "fixed_expense",
            Active = true,
            Tags = ["parceiro-regra"]
        });

        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var response = await client.GetFromJsonAsync<TagAnalyticsResponse>("/transactions/tags");

        Assert.NotNull(response);
        Assert.Contains("casa", response!.Tags);
        Assert.DoesNotContain("mercado", response.Tags);
        Assert.DoesNotContain("parceiro-regra", response.Tags);
    }

    [Fact]
    public async Task Tag_suggestions_endpoint_ranks_by_usage_count_and_includes_fixed_rule_tags()
    {
        await using var factory = new TestTransactionsFactory();
        factory.Aux.Seed(new PartnershipDocument
        {
            Id = "pair_1",
            UserId = "usr_gmarques",
            PartnerUserId = "usr_bmarques",
            Status = "active"
        });
        factory.Aux.Seed(new FixedRuleDocument
        {
            Id = "fxr_subscription",
            UserId = "usr_gmarques",
            Category = "fixed_expense",
            Active = true,
            Tags = ["assinatura"]
        });

        var market1 = Transaction("usr_gmarques", "tx_market_1", "2026-04", amount: 120m);
        market1.Tags = ["Mercado", "Casa"];
        var market2 = Transaction("usr_gmarques", "tx_market_2", "2026-03", amount: 90m);
        market2.Tags = ["mercado"];
        var partnerMarket = Transaction("usr_bmarques", "tx_partner_market", "2026-03", amount: 80m);
        partnerMarket.Tags = ["mercado"];
        var house = Transaction("usr_gmarques", "tx_house", "2026-04", amount: 200m);
        house.Tags = ["casa"];
        var deleted = Transaction("usr_gmarques", "tx_deleted", "2026-04", amount: 50m);
        deleted.Tags = ["mercado"];
        deleted.DeletedAt = DateTimeOffset.Parse("2026-04-30T12:00:00Z");

        factory.Transactions.Seed(market1);
        factory.Transactions.Seed(market2);
        factory.Transactions.Seed(partnerMarket);
        factory.Transactions.Seed(house);
        factory.Transactions.Seed(deleted);

        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var response = await client.GetFromJsonAsync<TagSuggestionsResponse>("/transactions/tags/suggestions");

        Assert.NotNull(response);
        var items = response!.Items;
        Assert.Equal("mercado", items[0].Tag);
        Assert.Equal(3, items[0].Count);
        Assert.Equal("casa", items[1].Tag);
        Assert.Equal(2, items[1].Count);

        var assinatura = Assert.Single(items.Where(x => x.Tag == "assinatura"));
        Assert.Equal(0, assinatura.Count);
        Assert.DoesNotContain(items, x => x.Tag == "Mercado");
    }

    [Fact]
    public async Task Tag_suggestions_endpoint_filters_by_prefix_and_limit()
    {
        await using var factory = new TestTransactionsFactory();
        var market = Transaction("usr_gmarques", "tx_market", "2026-04", amount: 120m);
        market.Tags = ["mercado"];
        var meals = Transaction("usr_gmarques", "tx_meals", "2026-04", amount: 60m);
        meals.Tags = ["mercearia"];
        var house = Transaction("usr_gmarques", "tx_house", "2026-04", amount: 200m);
        house.Tags = ["casa"];

        factory.Transactions.Seed(market);
        factory.Transactions.Seed(meals);
        factory.Transactions.Seed(house);

        using var client = factory.CreateHttpsClient();
        Authorize(client, factory, "usr_gmarques");

        var prefixed = await client.GetFromJsonAsync<TagSuggestionsResponse>("/transactions/tags/suggestions?prefix=Merc");
        Assert.NotNull(prefixed);
        Assert.Equal(2, prefixed!.Items.Count);
        Assert.All(prefixed.Items, x => Assert.StartsWith("merc", x.Tag, StringComparison.Ordinal));

        var limited = await client.GetFromJsonAsync<TagSuggestionsResponse>("/transactions/tags/suggestions?limit=1");
        Assert.NotNull(limited);
        Assert.Single(limited!.Items);
    }

    [Fact]
    public async Task Tag_suggestions_endpoint_requires_authentication()
    {
        await using var factory = new TestTransactionsFactory();
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/transactions/tags/suggestions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static void Authorize(HttpClient client, TestTransactionsFactory factory, string userId) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.IssueToken(userId));

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("code").GetString();
    }

    private static CardDocument Card(string userId, string id, string title = "Nubank Roxinho") =>
        new()
        {
            Id = id,
            UserId = userId,
            Title = title,
            ClosingDay = 28,
            DueDay = 5,
            Currency = "BRL",
            DeletedAt = null
        };

    private static TagSummary Summary(TagAnalyticsResponse response, string tag) =>
        Assert.Single(response.Items.Where(x => x.Tag == tag));

    private static TransactionDocument Transaction(
        string userId,
        string id,
        string yearMonth,
        string category = "variable_expense",
        string? cardId = null,
        decimal amount = 120m,
        string etag = "etag-1") =>
        new()
        {
            Id = id,
            UserId = userId,
            YearMonth = yearMonth,
            Date = DateOnly.Parse($"{yearMonth}-10"),
            PurchaseDate = category == "credit_card" ? DateOnly.Parse("2026-04-10") : null,
            Category = category,
            Kind = category == "income" ? "in" : "out",
            Description = "Mercado",
            Amount = amount,
            Currency = "BRL",
            CardId = cardId,
            Tags = [],
            Source = new TransactionSourceDocument { Channel = "manual" },
            CreatedAt = DateTimeOffset.Parse("2026-04-28T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-04-28T12:00:00Z"),
            DeletedAt = null,
            ETag = etag
        };
}
