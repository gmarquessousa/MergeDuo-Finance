namespace MergeDuo.Profile.Domain.Options;

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string Database { get; set; } = "mergeduo";
    public string UsersContainer { get; set; } = "users";
    public string TransactionsContainer { get; set; } = "transactions";
    public string PartnershipsContainer { get; set; } = "partnerships";
    public bool AutoCreateContainers { get; set; }
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "https://auth.mergeduo.app";
    public string Audience { get; set; } = "mergeduo-api";
    public string JwksUrl { get; set; } = "https://auth.mergeduo.app/.well-known/jwks.json";
    public string KeyId { get; set; } = "jwt-signing-rsa-v1";
    public string PublicKeyPem { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";
}

public sealed class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class StatsOptions
{
    public int StaleAfterMinutes { get; set; } = 60;
    public int DependencyTimeoutSeconds { get; set; } = 8;
}
