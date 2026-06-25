namespace MergeDuo.Identity.Domain.Options;

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string Database { get; set; } = "mergeduo";
    public string UsersContainer { get; set; } = "users";
    public string DevicesContainer { get; set; } = "devices";
    public string IdentityReservationsContainer { get; set; } = "identityReservations";
    public bool AutoCreateContainers { get; set; }
    public int? DatabaseThroughput { get; set; }
}

public sealed class GoogleOptions
{
    public string ClientId { get; set; } = "";
    public string JwksUrl { get; set; } = "https://www.googleapis.com/oauth2/v3/certs";
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "https://auth.mergeduo.app";
    public string Audience { get; set; } = "mergeduo-api";
    public string KeyId { get; set; } = "jwt-signing-rsa-v1";
    public string PrivateKeyPem { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
}

public sealed class RefreshTokenOptions
{
    public string CookieName { get; set; } = "md_refresh";
    public string? CookieDomain { get; set; }
    public string CookieSameSite { get; set; } = "None";
    public int LifetimeDays { get; set; } = 30;
    public string Pepper { get; set; } = "";
}

public sealed class PublicAppOptions
{
    public string BaseUrl { get; set; } = "";
}

public sealed class BlobStorageOptions
{
    public string ConnectionString { get; set; } = "";
    public string AccountUrl { get; set; } = "";
    public string AvatarsContainer { get; set; } = "avatars";
}

public sealed class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = [];
}
