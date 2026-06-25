using System.Text.Json;
using System.Text.Json.Serialization;

namespace MergeDuo.Cards.Domain.Contracts;

public sealed class CreateCardRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("closingDay")]
    public int? ClosingDay { get; set; }

    [JsonPropertyName("dueDay")]
    public int? DueDay { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class UpdateCardRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("closingDay")]
    public int? ClosingDay { get; set; }

    [JsonPropertyName("dueDay")]
    public int? DueDay { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed record CardResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("closingDay")] int ClosingDay,
    [property: JsonPropertyName("dueDay")] int DueDay,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("etag")] string? ETag);

public sealed record CardsListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<CardResponse> Items);

public sealed record BillingCycleResponse(
    [property: JsonPropertyName("closingDate")] DateOnly ClosingDate,
    [property: JsonPropertyName("dueDate")] DateOnly DueDate);

public sealed record CardUsageFreshnessResponse(
    [property: JsonPropertyName("retrievedAt")] DateTimeOffset RetrievedAt,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("isFresh")] bool IsFresh,
    [property: JsonPropertyName("isFallback")] bool IsFallback);

public sealed record CardUsageResponse(
    [property: JsonPropertyName("cardId")] string CardId,
    [property: JsonPropertyName("yearMonth")] string YearMonth,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount,
    [property: JsonPropertyName("transactionCount")] int TransactionCount,
    [property: JsonPropertyName("installmentCount")] int InstallmentCount,
    [property: JsonPropertyName("billingCycle")] BillingCycleResponse BillingCycle,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("freshness")] CardUsageFreshnessResponse? Freshness = null);

public sealed record CardUsageTotals(
    string CardId,
    string YearMonth,
    string Currency,
    decimal TotalAmount,
    int TransactionCount,
    int InstallmentCount);
