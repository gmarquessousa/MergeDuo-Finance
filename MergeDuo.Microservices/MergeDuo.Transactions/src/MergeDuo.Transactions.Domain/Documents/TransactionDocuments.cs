using System.Text.Json.Serialization;

namespace MergeDuo.Transactions.Domain.Documents;

public sealed class TransactionDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "transaction";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("yearMonth")]
    public string YearMonth { get; set; } = "";

    [JsonPropertyName("date")]
    public DateOnly Date { get; set; }

    [JsonPropertyName("purchaseDate")]
    public DateOnly? PurchaseDate { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "BRL";

    [JsonPropertyName("ownerLabel")]
    public string? OwnerLabel { get; set; }

    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

    [JsonPropertyName("fixedRuleId")]
    public string? FixedRuleId { get; set; }

    [JsonPropertyName("installments")]
    public InstallmentDocument? Installments { get; set; }

    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = [];

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("source")]
    public TransactionSourceDocument Source { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public sealed class InstallmentDocument
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("groupId")]
    public string GroupId { get; set; } = "";
}

public sealed class TransactionSourceDocument
{
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "manual";
}

public sealed class CardDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

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

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class FixedRuleDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = [];

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class PartnershipDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("partnerUserId")]
    public string PartnerUserId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}
