using System.Net.Http.Headers;
using System.Net.Http.Json;
using MergeDuo.Cards.Domain.Abstractions;
using MergeDuo.Cards.Domain.Contracts;
using MergeDuo.Cards.Domain.Exceptions;

namespace MergeDuo.Cards.Infra.Transactions;

public sealed class TransactionsUsageClient(HttpClient httpClient) : ICardUsageClient
{
    public async Task<CardUsageTotals> GetUsageAsync(
        string userId,
        string cardId,
        string yearMonth,
        string? authorizationHeader,
        CancellationToken cancellationToken)
    {
        var path =
            $"/internal/transactions/card-usage?cardId={Uri.EscapeDataString(cardId)}&ym={Uri.EscapeDataString(yearMonth)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("x-mergeduo-user-id", userId);

        if (!string.IsNullOrWhiteSpace(authorizationHeader)
            && AuthenticationHeaderValue.TryParse(authorizationHeader, out var authorization))
        {
            request.Headers.Authorization = authorization;
        }

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new CardsDependencyException("cards_dependency_unavailable", "Transactions Service unavailable.");
            }

            var payload = await response.Content.ReadFromJsonAsync<CardUsageTotalsResponse>(cancellationToken);
            if (payload is null
                || payload.CardId != cardId
                || payload.YearMonth != yearMonth
                || payload.Currency != "BRL")
            {
                throw new CardsDependencyException("cards_dependency_unavailable", "Invalid Transactions Service response.");
            }

            return new CardUsageTotals(
                payload.CardId,
                payload.YearMonth,
                payload.Currency,
                payload.TotalAmount,
                payload.TransactionCount,
                payload.InstallmentCount);
        }
        catch (CardsDependencyException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            throw new CardsDependencyException("cards_dependency_unavailable", "Transactions Service unavailable.", ex);
        }
    }

    private sealed record CardUsageTotalsResponse(
        string CardId,
        string YearMonth,
        string Currency,
        decimal TotalAmount,
        int TransactionCount,
        int InstallmentCount);
}
