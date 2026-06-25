namespace MergeDuo.Cards.Domain.Options;

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string Database { get; set; } = "mergeduo";
    public string CardsContainer { get; set; } = "cards";
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

public sealed class TransactionsServiceOptions
{
    public string BaseUrl { get; set; } = "https://mergeduo-transactions.internal";
    public int TimeoutSeconds { get; set; } = 5;
}

public sealed class RateLimitOptions
{
    public int GlobalPermitLimit { get; set; } = 300;
    public int CardReadPermitLimit { get; set; } = 120;
    public int CardCreatePermitLimit { get; set; } = 20;
    public int CardPatchPermitLimit { get; set; } = 60;
    public int CardDeletePermitLimit { get; set; } = 20;
    public int CardUsagePermitLimit { get; set; } = 60;
}
