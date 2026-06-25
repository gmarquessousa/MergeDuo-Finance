namespace MergeDuo.Partnership.Domain.Options;

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string Database { get; set; } = "mergeduo";
    public string UsersContainer { get; set; } = "users";
    public string InvitesContainer { get; set; } = "mergeInvites";
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

public sealed class PublicAppOptions
{
    public string InviteBaseUrl { get; set; } = "https://mergeduo.app/invites";
}

public sealed class InviteOptions
{
    public int ExpiresAfterHours { get; set; } = 72;
    public int TokenEntropyBytes { get; set; } = 32;
}

public sealed class RateLimitOptions
{
    public int GlobalPermitLimit { get; set; } = 300;
    public int InviteCreatePermitLimit { get; set; } = 20;
    public int InvitePreviewPermitLimit { get; set; } = 120;
    public int InviteAcceptPermitLimit { get; set; } = 30;
    public int PartnershipPermitLimit { get; set; } = 120;
}
