using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MergeDuo.Aggregates.Domain.Abstractions;
using MergeDuo.Aggregates.Tests.Fakes;
using MergeDuo.Aggregates.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Aggregates.Tests;

public sealed class TestAggregatesFactory : WebApplicationFactory<Program>
{
    private static readonly string SharedPrivateKeyPem = CreatePem();

    public InMemoryMonthlyAggregatesRepository Aggregates { get; } = new();
    public InMemoryTransactionsProjectionRepository Transactions { get; } = new();
    public InMemoryPartnershipsReadRepository Partnerships { get; } = new();
    public InMemoryUsersReadRepository Users { get; } = new();
    public InMemoryFixedRulesProjectionRepository FixedRules { get; } = new();
    public InMemoryCardsProjectionRepository Cards { get; } = new();
    public FakeReadinessProbe Readiness { get; } = new();
    public TestClock Clock { get; } = new(DateTimeOffset.Parse("2026-04-25T15:00:00Z"));
    public string PrivateKeyPem => SharedPrivateKeyPem;

    public TestAggregatesFactory()
    {
        Environment.SetEnvironmentVariable("Jwt__Issuer", "https://auth.mergeduo.app");
        Environment.SetEnvironmentVariable("Jwt__Audience", "mergeduo-api");
        Environment.SetEnvironmentVariable("Jwt__KeyId", "test-key");
        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPem", PrivateKeyPem);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "https://auth.mergeduo.app",
                ["Jwt:Audience"] = "mergeduo-api",
                ["Jwt:KeyId"] = "test-key",
                ["Jwt:PrivateKeyPem"] = PrivateKeyPem,
                ["Cosmos:Endpoint"] = "https://cosmos.test/",
                ["Cosmos:Database"] = "mergeduo",
                ["Cosmos:MonthlyAggregatesContainer"] = "monthlyAggregates",
                ["Cosmos:TransactionsContainer"] = "transactions",
                ["Cosmos:PartnershipsContainer"] = "partnerships",
                ["Cosmos:UsersContainer"] = "users",
                ["Cosmos:FixedRulesContainer"] = "fixedRules",
                ["Cosmos:CardsContainer"] = "cards",
                ["Cosmos:LeaseContainer"] = "transactions-leases",
                ["Cors:AllowedOrigins:0"] = "https://localhost",
                ["Aggregates:SourceVersion"] = "4",
                ["Aggregates:DependencyTimeoutSeconds"] = "8",
                ["Aggregates:MaxRebuildMonthsPerChange"] = "36",
                ["Aggregates:ProjectionMonthsAhead"] = "12",
                ["Aggregates:StaleAfterMinutes"] = "180",
                ["Aggregates:BusinessTimeZone"] = "America/Sao_Paulo",
                ["ChangeFeed:Enabled"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMonthlyAggregatesRepository>();
            services.RemoveAll<ITransactionsProjectionRepository>();
            services.RemoveAll<IPartnershipsReadRepository>();
            services.RemoveAll<IUsersReadRepository>();
            services.RemoveAll<IFixedRulesProjectionRepository>();
            services.RemoveAll<ICardsProjectionRepository>();
            services.RemoveAll<IAggregatesReadinessProbe>();
            services.RemoveAll<TimeProvider>();
            services.RemoveAll<IHostedService>();
            services.RemoveAll<CosmosClient>();

            services.AddSingleton<IMonthlyAggregatesRepository>(Aggregates);
            services.AddSingleton<ITransactionsProjectionRepository>(Transactions);
            services.AddSingleton<IPartnershipsReadRepository>(Partnerships);
            services.AddSingleton<IUsersReadRepository>(Users);
            services.AddSingleton<IFixedRulesProjectionRepository>(FixedRules);
            services.AddSingleton<ICardsProjectionRepository>(Cards);
            services.AddSingleton<IAggregatesReadinessProbe>(Readiness);
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    public string IssueToken(string userId)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(PrivateKeyPem);
        var key = new RsaSecurityKey(rsa)
        {
            KeyId = "test-key",
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: "https://auth.mergeduo.app",
            audience: "mergeduo-api",
            claims:
            [
                new Claim("userId", userId),
                new Claim("sub", userId),
                new Claim("deviceId", "dev_test")
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreatePem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }
}
