using System.Text.Json.Serialization;

namespace MergeDuo.Identity.Domain.Documents;

public sealed class DeviceDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "device";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "web";

    [JsonPropertyName("userAgent")]
    public string UserAgent { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("osVersion")]
    public string OsVersion { get; set; } = "";

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "";

    [JsonPropertyName("session")]
    public DeviceSession Session { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("lastSeenAt")]
    public DateTimeOffset LastSeenAt { get; set; }

    [JsonPropertyName("revokedAt")]
    public DateTimeOffset? RevokedAt { get; set; }

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; } = 7_776_000;

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public sealed class DeviceSession
{
    [JsonPropertyName("rememberMe")]
    public bool RememberMe { get; set; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("refreshTokenHash")]
    public string? RefreshTokenHash { get; set; }

    [JsonPropertyName("refreshTokenRotatedAt")]
    public DateTimeOffset? RefreshTokenRotatedAt { get; set; }

    [JsonPropertyName("refreshTokenExpiresAt")]
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }

    [JsonPropertyName("lastIp")]
    public string LastIp { get; set; } = "";

    [JsonPropertyName("lastLocation")]
    public string LastLocation { get; set; } = "";
}
