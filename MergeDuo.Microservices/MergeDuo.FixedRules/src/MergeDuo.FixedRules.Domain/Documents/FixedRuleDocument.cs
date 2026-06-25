using System.Text.Json.Serialization;

namespace MergeDuo.FixedRules.Domain.Documents;

public sealed class FixedRuleDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "fixedRule";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = [];

    [JsonPropertyName("schedule")]
    public FixedRuleScheduleDocument Schedule { get; set; } = new();

    [JsonPropertyName("startsAt")]
    public string StartsAt { get; set; } = "";

    [JsonPropertyName("endsAt")]
    public string? EndsAt { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;

    [JsonPropertyName("lastRunAt")]
    public string? LastRunAt { get; set; }

    [JsonPropertyName("nextRunAt")]
    public string? NextRunAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public sealed class FixedRuleScheduleDocument
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("day")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Day { get; set; }

    [JsonPropertyName("ordinal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Ordinal { get; set; }

    [JsonPropertyName("period")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Period { get; set; }
}

public sealed class CardProjection
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }
}
