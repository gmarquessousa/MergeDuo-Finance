namespace MergeDuo.Transactions.Domain.Options;

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string Database { get; set; } = "mergeduo";
    public string TransactionsContainer { get; set; } = "transactions";
    public string CardsContainer { get; set; } = "cards";
    public string FixedRulesContainer { get; set; } = "fixedRules";
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

public sealed class TransactionsOptions
{
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 100;
    public int MaxInstallments { get; set; } = 48;
    public int DependencyTimeoutSeconds { get; set; } = 5;
    public int TagSuggestionsDefaultLimit { get; set; } = 20;
    public int TagSuggestionsMaxLimit { get; set; } = 50;
    public string ContinuationTokenSecret { get; set; } = "dev-transactions-continuation-token-secret-change-me";
}

public sealed class InternalApiOptions
{
    public string SchedulerKey { get; set; } = "";
}

public sealed class RateLimitOptions
{
    public int GlobalPermitLimit { get; set; } = 300;
    public int ListPermitLimit { get; set; } = 120;
    public int GetPermitLimit { get; set; } = 180;
    public int CreatePermitLimit { get; set; } = 30;
    public int PatchPermitLimit { get; set; } = 60;
    public int DeletePermitLimit { get; set; } = 30;
    public int GroupReadPermitLimit { get; set; } = 60;
    public int GroupDeletePermitLimit { get; set; } = 20;
    public int CardUsagePermitLimit { get; set; } = 120;
}
