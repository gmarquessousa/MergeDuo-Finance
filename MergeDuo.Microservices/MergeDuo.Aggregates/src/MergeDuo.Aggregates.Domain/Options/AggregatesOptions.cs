namespace MergeDuo.Aggregates.Domain.Options;

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string Database { get; set; } = "mergeduo";
    public string MonthlyAggregatesContainer { get; set; } = "monthlyAggregates";
    public string TransactionsContainer { get; set; } = "transactions";
    public string PartnershipsContainer { get; set; } = "partnerships";
    public string UsersContainer { get; set; } = "users";
    public string FixedRulesContainer { get; set; } = "fixedRules";
    public string CardsContainer { get; set; } = "cards";
    public string LeaseContainer { get; set; } = "transactions-leases";
    public bool AutoCreateContainers { get; set; }
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "https://auth.mergeduo.app";
    public string Audience { get; set; } = "mergeduo-api";
    public string JwksUrl { get; set; } = "";
    public string KeyId { get; set; } = "jwt-signing-rsa-v1";
    public string PublicKeyPem { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";
}

public sealed class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class AggregatesOptions
{
    public int SourceVersion { get; set; } = 4;
    public int DependencyTimeoutSeconds { get; set; } = 8;
    public int MaxRebuildMonthsPerChange { get; set; } = 36;
    public int ProjectionMonthsAhead { get; set; } = 12;
    public int StaleAfterMinutes { get; set; } = 180;
    public string BusinessTimeZone { get; set; } = "America/Sao_Paulo";
}

public sealed class ChangeFeedOptions
{
    public bool Enabled { get; set; } = true;
    public string ProcessorName { get; set; } = "mergeduo-aggregates-transactions";
    public string InstanceName { get; set; } = Environment.MachineName;
    public int MaxItemsPerInvocation { get; set; } = 100;
}

public sealed class RateLimitOptions
{
    public int GlobalPermitLimit { get; set; } = 300;
    public int MonthPermitLimit { get; set; } = 120;
    public int YearPermitLimit { get; set; } = 60;
    public int PartnerMonthPermitLimit { get; set; } = 60;
    public int PartnerYearPermitLimit { get; set; } = 30;
}
