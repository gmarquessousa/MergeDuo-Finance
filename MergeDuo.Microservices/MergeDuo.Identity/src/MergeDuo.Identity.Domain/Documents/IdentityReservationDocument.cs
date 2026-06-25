using System.Text.Json.Serialization;

namespace MergeDuo.Identity.Domain.Documents;

public sealed class IdentityReservationDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "identityReservation";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("valueHash")]
    public string ValueHash { get; set; } = "";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
