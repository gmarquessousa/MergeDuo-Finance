using System.Text.Json.Serialization;

namespace MergeDuo.Cards.Domain.Documents;

public sealed class CardDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "card";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("closingDay")]
    public int ClosingDay { get; set; }

    [JsonPropertyName("dueDay")]
    public int DueDay { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "BRL";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
