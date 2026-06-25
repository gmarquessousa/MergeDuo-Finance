using System.Security.Cryptography;
using MergeDuo.Identity.Domain.Abstractions;
using MergeDuo.Identity.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MergeDuo.Identity.Tests;

public sealed class TestIdentityFactory : WebApplicationFactory<Program>
{
    public InMemoryUsersRepository Users { get; } = new();
    public InMemoryDevicesRepository Devices { get; } = new();
    public FakeGoogleIdTokenValidator Google { get; } = new();
    public FakeAvatarStorage AvatarStorage { get; } = new();
    public string PrivateKeyPem { get; } = CreatePem();

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
                ["Jwt:AccessTokenMinutes"] = "15",
                ["RefreshTokens:CookieName"] = "md_refresh",
                ["RefreshTokens:CookieSameSite"] = "Lax",
                ["RefreshTokens:LifetimeDays"] = "30",
                ["RefreshTokens:Pepper"] = "test-pepper",
                ["PublicApp:BaseUrl"] = "",
                ["Google:ClientId"] = "test-client",
                ["Google:JwksUrl"] = "https://google.test/jwks",
                ["Cosmos:Endpoint"] = "https://cosmos.test/",
                ["Cosmos:Database"] = "mergeduo",
                ["Cosmos:UsersContainer"] = "users",
                ["Cosmos:DevicesContainer"] = "devices",
                ["Cosmos:IdentityReservationsContainer"] = "identityReservations",
                ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["BlobStorage:AccountUrl"] = "https://stmergeduo.blob.core.windows.net/",
                ["BlobStorage:AvatarsContainer"] = "avatars",
                ["Cors:AllowedOrigins:0"] = "https://localhost"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUsersRepository>();
            services.RemoveAll<IDevicesRepository>();
            services.RemoveAll<IGoogleIdTokenValidator>();
            services.RemoveAll<IAvatarStorage>();
            services.AddSingleton<IUsersRepository>(Users);
            services.AddSingleton<IDevicesRepository>(Devices);
            services.AddSingleton<IGoogleIdTokenValidator>(Google);
            services.AddSingleton<IAvatarStorage>(AvatarStorage);
        });
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private static string CreatePem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }
}
