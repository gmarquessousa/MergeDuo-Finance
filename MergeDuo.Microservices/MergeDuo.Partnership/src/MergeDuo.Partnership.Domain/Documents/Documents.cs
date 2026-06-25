using System.Text.Json.Serialization;

namespace MergeDuo.Partnership.Domain.Documents;

public sealed class MergeInviteDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "mergeInvite";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("inviterUserId")]
    public string InviterUserId { get; set; } = "";

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = InviteStatuses.Pending;

    [JsonPropertyName("inviterSnapshot")]
    public PartnerSnapshot InviterSnapshot { get; set; } = new();

    [JsonPropertyName("acceptedBy")]
    public AcceptedBySnapshot? AcceptedBy { get; set; }

    [JsonPropertyName("partnershipId")]
    public string? PartnershipId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonPropertyName("acceptedAt")]
    public DateTimeOffset? AcceptedAt { get; set; }

    [JsonPropertyName("revokedAt")]
    public DateTimeOffset? RevokedAt { get; set; }

    [JsonPropertyName("ttl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Ttl { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public sealed class PartnershipDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "partnership";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("partnershipId")]
    public string PartnershipId { get; set; } = "";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("partnerUserId")]
    public string PartnerUserId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = PartnershipStatuses.Active;

    [JsonPropertyName("partnerSnapshot")]
    public PartnerSnapshot PartnerSnapshot { get; set; } = new();

    [JsonPropertyName("startingBalance")]
    public decimal StartingBalance { get; set; }

    [JsonPropertyName("mergedSince")]
    public DateOnly MergedSince { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("endedAt")]
    public DateTimeOffset? EndedAt { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public sealed class UserSummaryDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "user";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = "";

    [JsonPropertyName("avatarInitials")]
    public string AvatarInitials { get; set; } = "";

    [JsonPropertyName("initials")]
    public string? Initials { get; set; }

    [JsonPropertyName("financial")]
    public UserFinancial? Financial { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public sealed class UserFinancial
{
    [JsonPropertyName("startingBalance")]
    public decimal StartingBalance { get; set; }
}

public sealed class PartnerSnapshot
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = "";

    [JsonPropertyName("initials")]
    public string Initials { get; set; } = "";
}

public sealed class AcceptedBySnapshot
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = "";

    [JsonPropertyName("acceptedAt")]
    public DateTimeOffset AcceptedAt { get; set; }
}

public static class InviteStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Revoked = "revoked";
    public const string Expired = "expired";
}

public static class PartnershipStatuses
{
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Ended = "ended";
}
