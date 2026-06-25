using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MergeDuo.Cards.Domain.Abstractions;
using MergeDuo.Cards.Tests.Fakes;
using MergeDuo.Cards.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace MergeDuo.Cards.Tests;

public sealed class TestCardsFactory : WebApplicationFactory<Program>
{
    private static readonly string SharedPrivateKeyPem = CreatePem();

    public InMemoryCardsRepository Cards { get; } = new();
    public FakeUsageClient Usage { get; } = new();
    public FakeReadinessProbe Readiness { get; } = new();
    public TestClock Clock { get; } = new(DateTimeOffset.Parse("2026-04-27T12:00:00Z"));
    public string PrivateKeyPem => SharedPrivateKeyPem;

    public TestCardsFactory()
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
                ["Cosmos:CardsContainer"] = "cards",
                ["Cors:AllowedOrigins:0"] = "https://localhost",
                ["TransactionsService:BaseUrl"] = "https://transactions.test",
                ["TransactionsService:TimeoutSeconds"] = "5"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.RemoveAll<ICardsRepository>();
            services.RemoveAll<ICardUsageClient>();
            services.RemoveAll<ICardsReadinessProbe>();
            services.RemoveAll<ICardIdGenerator>();

            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ICardsRepository>(Cards);
            services.AddSingleton<ICardUsageClient>(Usage);
            services.AddSingleton<ICardsReadinessProbe>(Readiness);
            services.AddSingleton<ICardIdGenerator>(new FakeCardIdGenerator("card_test_01", "card_test_02"));
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
