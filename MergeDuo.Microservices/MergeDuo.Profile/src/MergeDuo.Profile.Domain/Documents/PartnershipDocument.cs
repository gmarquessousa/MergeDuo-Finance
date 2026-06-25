using System.Text.Json.Serialization;

namespace MergeDuo.Profile.Domain.Documents;

public sealed class PartnershipDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "partnership";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("partnerUserId")]
    public string PartnerUserId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("mergedSince")]
    public string MergedSince { get; set; } = "";

    [JsonPropertyName("endedAt")]
    public DateTimeOffset? EndedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TransactionStatsProjection
{
    [JsonPropertyName("yearMonth")]
    public string YearMonth { get; set; } = "";
}
