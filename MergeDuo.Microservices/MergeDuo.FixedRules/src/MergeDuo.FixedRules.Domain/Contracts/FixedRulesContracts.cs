using System.Text.Json;
using System.Text.Json.Serialization;
using MergeDuo.FixedRules.Domain.Documents;

namespace MergeDuo.FixedRules.Domain.Contracts;

public sealed class CreateFixedRuleRequest
{
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonPropertyName("schedule")]
    public FixedRuleScheduleRequest? Schedule { get; set; }

    [JsonPropertyName("startsAt")]
    public string? StartsAt { get; set; }

    [JsonPropertyName("endsAt")]
    public string? EndsAt { get; set; }

    [JsonPropertyName("active")]
    public bool? Active { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class UpdateFixedRuleRequest
{
    [JsonPropertyName("category")]
    public JsonElement Category { get; set; }

    [JsonPropertyName("description")]
    public JsonElement Description { get; set; }

    [JsonPropertyName("amount")]
    public JsonElement Amount { get; set; }

    [JsonPropertyName("cardId")]
    public JsonElement CardId { get; set; }

    [JsonPropertyName("tags")]
    public JsonElement Tags { get; set; }

    [JsonPropertyName("schedule")]
    public JsonElement Schedule { get; set; }

    [JsonPropertyName("startsAt")]
    public JsonElement StartsAt { get; set; }

    [JsonPropertyName("endsAt")]
    public JsonElement EndsAt { get; set; }

    [JsonPropertyName("active")]
    public JsonElement Active { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }

    [JsonIgnore]
    public bool HasAnyEditableField =>
        Category.ValueKind != JsonValueKind.Undefined ||
        Description.ValueKind != JsonValueKind.Undefined ||
        Amount.ValueKind != JsonValueKind.Undefined ||
        CardId.ValueKind != JsonValueKind.Undefined ||
        Tags.ValueKind != JsonValueKind.Undefined ||
        Schedule.ValueKind != JsonValueKind.Undefined ||
        StartsAt.ValueKind != JsonValueKind.Undefined ||
        EndsAt.ValueKind != JsonValueKind.Undefined ||
        Active.ValueKind != JsonValueKind.Undefined;
}

public sealed class FixedRuleScheduleRequest
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("day")]
    public int? Day { get; set; }

    [JsonPropertyName("ordinal")]
    public int? Ordinal { get; set; }

    [JsonPropertyName("period")]
    public string? Period { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed record FixedRuleResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("cardId")] string? CardId,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("schedule")] FixedRuleScheduleDocument Schedule,
    [property: JsonPropertyName("startsAt")] string StartsAt,
    [property: JsonPropertyName("endsAt")] string? EndsAt,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("lastRunAt")] string? LastRunAt,
    [property: JsonPropertyName("nextRunAt")] string? NextRunAt,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("etag")] string? ETag,
    [property: JsonPropertyName("warnings")] IReadOnlyList<FixedRuleWarningResponse>? Warnings = null);

public sealed record FixedRuleWarningResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("severity")] string Severity);

public sealed record FixedRulesListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<FixedRuleResponse> Items);

public sealed record FixedRulePreviewResponse(
    [property: JsonPropertyName("ruleId")] string RuleId,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("items")] IReadOnlyList<FixedRuleOccurrenceResponse> Items,
    [property: JsonPropertyName("warnings")] IReadOnlyList<FixedRuleWarningResponse>? Warnings = null);

public sealed record FixedRuleOccurrenceResponse(
    [property: JsonPropertyName("occurrenceDate")] string OccurrenceDate,
    [property: JsonPropertyName("yearMonth")] string YearMonth,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("cardId")] string? CardId,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("warnings")] IReadOnlyList<FixedRuleWarningResponse>? Warnings = null);
