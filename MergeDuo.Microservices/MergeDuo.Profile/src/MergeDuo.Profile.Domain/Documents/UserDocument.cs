using System.Text.Json.Serialization;

namespace MergeDuo.Profile.Domain.Documents;

public sealed class UserDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "user";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("avatarInitials")]
    public string AvatarInitials { get; set; } = "";

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("memberSince")]
    public string MemberSince { get; set; } = "";

    [JsonPropertyName("registeredAt")]
    public string RegisteredAt { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonPropertyName("stats")]
    public UserStats? Stats { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public sealed class UserStats
{
    [JsonPropertyName("transactionsTracked")]
    public int TransactionsTracked { get; set; }

    [JsonPropertyName("activeMonths")]
    public int ActiveMonths { get; set; }

    [JsonPropertyName("lastRecomputedAt")]
    public DateTimeOffset? LastRecomputedAt { get; set; }
}
