namespace MergeDuo.FixedRules.Domain.Options;

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string Database { get; set; } = "mergeduo";
    public string FixedRulesContainer { get; set; } = "fixedRules";
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

public sealed class PreviewOptions
{
    public int MaxMonths { get; set; } = 24;
    public string BusinessCalendar { get; set; } = "WeekendOnly";
}

public sealed class RateLimitOptions
{
    public int GlobalPermitLimit { get; set; } = 300;
    public int FixedRuleReadPermitLimit { get; set; } = 120;
    public int FixedRuleCreatePermitLimit { get; set; } = 30;
    public int FixedRulePatchPermitLimit { get; set; } = 60;
    public int FixedRuleDeletePermitLimit { get; set; } = 30;
    public int FixedRulePreviewPermitLimit { get; set; } = 60;
}
