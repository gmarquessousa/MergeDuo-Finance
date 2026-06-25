using System.Net;
using MergeDuo.Cards.Domain.Abstractions;
using MergeDuo.Cards.Domain.Documents;
using MergeDuo.Cards.Domain.Exceptions;
using MergeDuo.Cards.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Cards.Infra.Cosmos;

public sealed class CardsRepository : ICardsRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public CardsRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.CardsContainer);
        _diagnostics = diagnostics;
    }

    public async Task<IReadOnlyList<CardDocument>> ListActiveAsync(string userId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT * FROM c
                WHERE c.userId = @userId
                  AND IS_NULL(c.deletedAt)
                ORDER BY c.createdAt DESC
                """)
            .WithParameter("@userId", userId);

        try
        {
            using var iterator = _container.GetItemQueryIterator<CardDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(userId),
                    MaxItemCount = 100
                });

            var results = new List<CardDocument>();
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("cards", "list_active", page.RequestCharge, throttled: false);
                results.AddRange(page.Resource);
            }

            await EnsureEtagsAsync(results, cancellationToken);

            return results;
        }
        catch (CosmosException ex)
        {
            RecordFailure("list_active", ex);
            throw new CardsDependencyException("cards_dependency_unavailable", "Cards dependency unavailable.", ex);
        }
    }

    public async Task<CardDocument?> GetByIdAsync(
        string userId,
        string cardId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<CardDocument>(
                cardId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("cards", "read_item", response.RequestCharge, throttled: false);
            response.Resource.ETag = response.ETag;

            if (!includeDeleted && response.Resource.DeletedAt is not null)
            {
                return null;
            }

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            RecordFailure("read_item", ex);
            throw new CardsDependencyException("cards_dependency_unavailable", "Cards dependency unavailable.", ex);
        }
    }

    public async Task CreateAsync(CardDocument card, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.CreateItemAsync(
                card,
                new PartitionKey(card.UserId),
                requestOptions: new ItemRequestOptions { EnableContentResponseOnWrite = false },
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("cards", "create_item", response.RequestCharge, throttled: false);
            card.ETag = response.ETag;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new CardsConflictException("card_conflict", "Card conflict.");
        }
        catch (CosmosException ex)
        {
            RecordFailure("create_item", ex);
            throw new CardsDependencyException("cards_dependency_unavailable", "Cards dependency unavailable.", ex);
        }
    }

    public async Task<CardDocument> PatchAsync(
        CardDocument card,
        CardPatch patch,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken)
    {
        var operations = new List<PatchOperation>();
        if (patch.Title is not null)
        {
            operations.Add(PatchOperation.Set("/title", patch.Title));
        }

        if (patch.ClosingDay is not null)
        {
            operations.Add(PatchOperation.Set("/closingDay", patch.ClosingDay.Value));
        }

        if (patch.DueDay is not null)
        {
            operations.Add(PatchOperation.Set("/dueDay", patch.DueDay.Value));
        }

        if (patch.Currency is not null)
        {
            operations.Add(PatchOperation.Set("/currency", patch.Currency));
        }

        operations.Add(PatchOperation.Set("/updatedAt", patch.UpdatedAt));

        var options = new PatchItemRequestOptions
        {
            IfMatchEtag = ifMatchEtag,
            EnableContentResponseOnWrite = true
        };

        try
        {
            var response = await _container.PatchItemAsync<CardDocument>(
                card.Id,
                new PartitionKey(card.UserId),
                operations,
                options,
                cancellationToken);
            _diagnostics.RecordCosmosOperation("cards", "patch_item", response.RequestCharge, throttled: false);
            response.Resource.ETag = response.ETag;
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw clientProvidedEtag
                ? new CardsPreconditionFailedException("precondition_failed", "Card precondition failed.")
                : new CardsConflictException("card_conflict", "Card update conflicted.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new CardsNotFoundException("card_not_found", "Card not found.");
        }
        catch (CosmosException ex)
        {
            RecordFailure("patch_item", ex);
            throw new CardsDependencyException("cards_dependency_unavailable", "Cards dependency unavailable.", ex);
        }
    }

    public async Task SoftDeleteAsync(
        CardDocument card,
        DateTimeOffset deletedAt,
        string ifMatchEtag,
        bool clientProvidedEtag,
        CancellationToken cancellationToken)
    {
        var operations = new[]
        {
            PatchOperation.Set("/deletedAt", deletedAt),
            PatchOperation.Set("/updatedAt", deletedAt)
        };

        var options = new PatchItemRequestOptions
        {
            IfMatchEtag = ifMatchEtag,
            EnableContentResponseOnWrite = false
        };

        try
        {
            var response = await _container.PatchItemAsync<CardDocument>(
                card.Id,
                new PartitionKey(card.UserId),
                operations,
                options,
                cancellationToken);
            _diagnostics.RecordCosmosOperation("cards", "soft_delete", response.RequestCharge, throttled: false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw clientProvidedEtag
                ? new CardsPreconditionFailedException("precondition_failed", "Card precondition failed.")
                : new CardsConflictException("card_conflict", "Card delete conflicted.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new CardsNotFoundException("card_not_found", "Card not found.");
        }
        catch (CosmosException ex)
        {
            RecordFailure("soft_delete", ex);
            throw new CardsDependencyException("cards_dependency_unavailable", "Cards dependency unavailable.", ex);
        }
    }

    private void RecordFailure(string operation, CosmosException ex) =>
        _diagnostics.RecordCosmosOperation("cards", operation, ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);

    private async Task EnsureEtagsAsync(IEnumerable<CardDocument> cards, CancellationToken cancellationToken)
    {
        foreach (var card in cards)
        {
            if (!string.IsNullOrWhiteSpace(card.ETag))
            {
                continue;
            }

            try
            {
                var response = await _container.ReadItemAsync<CardDocument>(
                    card.Id,
                    new PartitionKey(card.UserId),
                    cancellationToken: cancellationToken);
                _diagnostics.RecordCosmosOperation("cards", "list_read_etag", response.RequestCharge, throttled: false);
                card.ETag = response.ETag;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // The item changed between query and ETag hydration. Keep the list response best-effort.
            }
        }
    }
}
