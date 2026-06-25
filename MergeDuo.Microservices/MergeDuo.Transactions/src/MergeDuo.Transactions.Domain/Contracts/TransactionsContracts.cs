using System.Text.Json;
using System.Text.Json.Serialization;

namespace MergeDuo.Transactions.Domain.Contracts;

public sealed class CreateTransactionRequest
{
    [JsonPropertyName("date")]
    public DateOnly? Date { get; set; }

    [JsonPropertyName("purchaseDate")]
    public DateOnly? PurchaseDate { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("ownerLabel")]
    public string? OwnerLabel { get; set; }

    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

    [JsonPropertyName("fixedRuleId")]
    public string? FixedRuleId { get; set; }

    [JsonPropertyName("installments")]
    public CreateInstallmentsRequest? Installments { get; set; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class InternalCreateTransactionRequest
{
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("transaction")]
    public CreateTransactionRequest? Transaction { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class UpdateTransactionRequest
{
    [JsonPropertyName("date")]
    public DateOnly? Date { get; set; }

    [JsonPropertyName("purchaseDate")]
    public DateOnly? PurchaseDate { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("ownerLabel")]
    public string? OwnerLabel { get; set; }

    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

    [JsonPropertyName("fixedRuleId")]
    public string? FixedRuleId { get; set; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class CreateInstallmentsRequest
{
    [JsonPropertyName("total")]
    public int? Total { get; set; }
}

public sealed record TransactionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("yearMonth")] string YearMonth,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("purchaseDate")] DateOnly? PurchaseDate,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("ownerLabel")] string? OwnerLabel,
    [property: JsonPropertyName("cardId")] string? CardId,
    [property: JsonPropertyName("cardTitle")] string? CardTitle,
    [property: JsonPropertyName("fixedRuleId")] string? FixedRuleId,
    [property: JsonPropertyName("installments")] InstallmentResponse? Installments,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("source")] TransactionSourceResponse Source,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("etag")] string? ETag);

public sealed record InstallmentResponse(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("groupId")] string GroupId);

public sealed record TransactionSourceResponse(
    [property: JsonPropertyName("channel")] string Channel);

public sealed record TransactionsListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<TransactionResponse> Items,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken);

public sealed record TagAnalyticsResponse(
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("items")] IReadOnlyList<TagSummary> Items);

public sealed record TagSummary(
    [property: JsonPropertyName("tag")] string Tag,
    [property: JsonPropertyName("expensesTotal")] decimal ExpensesTotal,
    [property: JsonPropertyName("transactionCount")] int TransactionCount,
    [property: JsonPropertyName("transactions")] IReadOnlyList<TransactionResponse>? Transactions);

public sealed record TagSuggestionsResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<TagSuggestion> Items);

public sealed record TagSuggestion(
    [property: JsonPropertyName("tag")] string Tag,
    [property: JsonPropertyName("count")] int Count);

public sealed record CreateTransactionsResponse(
    [property: JsonPropertyName("groupId")] string? GroupId,
    [property: JsonPropertyName("items")] IReadOnlyList<TransactionResponse> Items);

public sealed record TransactionGroupResponse(
    [property: JsonPropertyName("groupId")] string GroupId,
    [property: JsonPropertyName("items")] IReadOnlyList<TransactionResponse> Items);

public sealed record DeleteTransactionGroupResponse(
    [property: JsonPropertyName("groupId")] string GroupId,
    [property: JsonPropertyName("deletedCount")] int DeletedCount,
    [property: JsonPropertyName("skippedCount")] int SkippedCount);

public sealed record CardUsageResponse(
    [property: JsonPropertyName("cardId")] string CardId,
    [property: JsonPropertyName("yearMonth")] string YearMonth,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount,
    [property: JsonPropertyName("transactionCount")] int TransactionCount,
    [property: JsonPropertyName("installmentCount")] int InstallmentCount);
