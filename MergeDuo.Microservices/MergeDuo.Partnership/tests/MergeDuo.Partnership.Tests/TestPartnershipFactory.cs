using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MergeDuo.Partnership.Domain.Abstractions;
using MergeDuo.Partnership.Tests.Fakes;
using MergeDuo.Partnership.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace MergeDuo.Partnership.Tests;

public sealed class TestPartnershipFactory : WebApplicationFactory<Program>
{
    private static readonly string SharedPrivateKeyPem = CreatePem();

    public InMemoryUsersReadRepository Users { get; } = new();
    public InMemoryInvitesRepository Invites { get; } = new();
    public InMemoryPartnershipsRepository Partnerships { get; } = new();
    public FakeReadinessProbe Readiness { get; } = new();
    public TestClock Clock { get; } = new(DateTimeOffset.Parse("2026-04-27T12:00:00Z"));
    public string PrivateKeyPem => SharedPrivateKeyPem;

    public TestPartnershipFactory()
    {
        Environment.SetEnvironmentVariable("Jwt__Issuer", "https://auth.mergeduo.app");
        Environment.SetEnvironmentVariable("Jwt__Audience", "mergeduo-api");
        Environment.SetEnvironmentVariable("Jwt__KeyId", "test-key");
        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPem", PrivateKeyPem);
        Environment.SetEnvironmentVariable("PublicApp__InviteBaseUrl", "https://app.test/invites");
        Environment.SetEnvironmentVariable("Invite__ExpiresAfterHours", "72");
        Environment.SetEnvironmentVariable("Invite__TokenEntropyBytes", "32");
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
                ["Cosmos:InvitesContainer"] = "mergeInvites",
                ["Cosmos:PartnershipsContainer"] = "partnerships",
                ["Cors:AllowedOrigins:0"] = "https://localhost",
                ["PublicApp:InviteBaseUrl"] = "https://app.test/invites",
                ["Invite:ExpiresAfterHours"] = "72",
                ["Invite:TokenEntropyBytes"] = "32"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.RemoveAll<IUsersReadRepository>();
            services.RemoveAll<IInvitesRepository>();
            services.RemoveAll<IPartnershipsRepository>();
            services.RemoveAll<IPartnershipReadinessProbe>();

            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IUsersReadRepository>(Users);
            services.AddSingleton<IInvitesRepository>(Invites);
            services.AddSingleton<IPartnershipsRepository>(Partnerships);
            services.AddSingleton<IPartnershipReadinessProbe>(Readiness);
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
