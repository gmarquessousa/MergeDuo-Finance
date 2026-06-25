using System.Text.Json.Serialization;

namespace MergeDuo.Identity.Domain.Documents;

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

    [JsonPropertyName("financial")]
    public UserFinancial Financial { get; set; } = new();

    [JsonPropertyName("preferences")]
    public UserPreferences Preferences { get; set; } = new();

    [JsonPropertyName("stats")]
    public UserStats Stats { get; set; } = new();

    [JsonPropertyName("auth")]
    public UserAuth Auth { get; set; } = new();

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public sealed class UserFinancial
{
    [JsonPropertyName("startingBalance")]
    public decimal StartingBalance { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "BRL";
}

public sealed class UserPreferences
{
    [JsonPropertyName("darkMode")]
    public bool DarkMode { get; set; }

    [JsonPropertyName("weeklyReport")]
    public bool WeeklyReport { get; set; } = true;
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

public sealed class UserAuth
{
    [JsonPropertyName("google")]
    public GoogleAuthState Google { get; set; } = new();

    [JsonPropertyName("lastLoginAt")]
    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class GoogleAuthState
{
    [JsonPropertyName("sub")]
    public string Sub { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("emailVerified")]
    public bool EmailVerified { get; set; }

    [JsonPropertyName("hostedDomain")]
    public string? HostedDomain { get; set; }

    [JsonPropertyName("pictureUrl")]
    public string? PictureUrl { get; set; }

    [JsonPropertyName("linkedAt")]
    public DateTimeOffset LinkedAt { get; set; }
}
