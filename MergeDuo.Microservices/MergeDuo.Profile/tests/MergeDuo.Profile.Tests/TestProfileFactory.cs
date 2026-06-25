using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MergeDuo.Profile.Domain.Abstractions;
using MergeDuo.Profile.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace MergeDuo.Profile.Tests;

public sealed class TestProfileFactory : WebApplicationFactory<Program>
{
    private static readonly string SharedPrivateKeyPem = CreatePem();

    public InMemoryUsersRepository Users { get; } = new();
    public InMemoryPartnershipsRepository Partnerships { get; } = new();
    public InMemoryTransactionsStatsRepository Transactions { get; } = new();
    public FakeReadinessProbe Readiness { get; } = new();
    public string PrivateKeyPem => SharedPrivateKeyPem;

    public TestProfileFactory()
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
                ["Cosmos:UsersContainer"] = "users",
                ["Cosmos:TransactionsContainer"] = "transactions",
                ["Cosmos:PartnershipsContainer"] = "partnerships",
                ["Cors:AllowedOrigins:0"] = "https://localhost",
                ["Stats:StaleAfterMinutes"] = "60",
                ["Stats:DependencyTimeoutSeconds"] = "8"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUsersRepository>();
            services.RemoveAll<IPartnershipsRepository>();
            services.RemoveAll<ITransactionsStatsRepository>();
            services.RemoveAll<IProfileReadinessProbe>();

            services.AddSingleton<IUsersRepository>(Users);
            services.AddSingleton<IPartnershipsRepository>(Partnerships);
            services.AddSingleton<ITransactionsStatsRepository>(Transactions);
            services.AddSingleton<IProfileReadinessProbe>(Readiness);
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
