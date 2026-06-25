using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MergeDuo.Transactions.Domain.Abstractions;
using MergeDuo.Transactions.Domain.Options;
using MergeDuo.Transactions.Tests.Fakes;
using MergeDuo.Transactions.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace MergeDuo.Transactions.Tests;

public sealed class TestTransactionsFactory : WebApplicationFactory<Program>
{
    private static readonly string SharedPrivateKeyPem = CreatePem();

    public InMemoryTransactionsRepository Transactions { get; } = new();
    public InMemoryAuxRepositories Aux { get; } = new();
    public FakeReadinessProbe Readiness { get; } = new();
    public TestClock Clock { get; } = new(DateTimeOffset.Parse("2026-04-28T12:00:00Z"));
    public string PrivateKeyPem => SharedPrivateKeyPem;

    public TestTransactionsFactory()
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
                ["Cosmos:TransactionsContainer"] = "transactions",
                ["Cosmos:CardsContainer"] = "cards",
                ["Cosmos:FixedRulesContainer"] = "fixedRules",
                ["Cosmos:PartnershipsContainer"] = "partnerships",
                ["Cors:AllowedOrigins:0"] = "https://localhost",
                ["Transactions:DefaultPageSize"] = "50",
                ["Transactions:MaxPageSize"] = "100",
                ["Transactions:MaxInstallments"] = "48",
                ["InternalApi:SchedulerKey"] = "test-scheduler-key"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.RemoveAll<ITransactionsRepository>();
            services.RemoveAll<ICardsReadRepository>();
            services.RemoveAll<IFixedRulesReadRepository>();
            services.RemoveAll<IPartnershipsReadRepository>();
            services.RemoveAll<ITransactionsReadinessProbe>();
            services.RemoveAll<ITransactionIdGenerator>();
            services.RemoveAll<InternalApiOptions>();

            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ITransactionsRepository>(Transactions);
            services.AddSingleton<ICardsReadRepository>(Aux);
            services.AddSingleton<IFixedRulesReadRepository>(Aux);
            services.AddSingleton<IPartnershipsReadRepository>(Aux);
            services.AddSingleton<ITransactionsReadinessProbe>(Readiness);
            services.AddSingleton<ITransactionIdGenerator>(new FakeTransactionIdGenerator("tx_test_01", "tx_test_02", "tx_test_03"));
            services.AddSingleton(new InternalApiOptions { SchedulerKey = "test-scheduler-key" });
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
