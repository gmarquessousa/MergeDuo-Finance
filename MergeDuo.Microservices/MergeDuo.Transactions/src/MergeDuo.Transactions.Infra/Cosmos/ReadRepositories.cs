using System.Net;
using MergeDuo.Transactions.Domain.Abstractions;
using MergeDuo.Transactions.Domain.Documents;
using MergeDuo.Transactions.Domain.Exceptions;
using MergeDuo.Transactions.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Transactions.Infra.Cosmos;

public sealed class CardsReadRepository : ICardsReadRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public CardsReadRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.CardsContainer);
        _diagnostics = diagnostics;
    }

    public async Task<CardDocument?> GetActiveAsync(string userId, string cardId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<CardDocument>(cardId, new PartitionKey(userId), cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("cards", "read_item", response.RequestCharge, throttled: false);
            return response.Resource.DeletedAt is null ? response.Resource : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            RecordFailure("cards", "read_item", ex);
            throw Dependency(ex);
        }
    }

    private void RecordFailure(string container, string operation, CosmosException ex) =>
        _diagnostics.RecordCosmosOperation(container, operation, ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);

    private static TransactionsDependencyException Dependency(Exception ex) =>
        new("transactions_dependency_unavailable", "Transactions dependency unavailable.", ex);
}

public sealed class FixedRulesReadRepository : IFixedRulesReadRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public FixedRulesReadRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.FixedRulesContainer);
        _diagnostics = diagnostics;
    }

    public async Task<FixedRuleDocument?> GetActiveAsync(string userId, string fixedRuleId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<FixedRuleDocument>(fixedRuleId, new PartitionKey(userId), cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("fixedRules", "read_item", response.RequestCharge, throttled: false);
            return response.Resource.DeletedAt is null && response.Resource.Active ? response.Resource : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            RecordFailure("fixedRules", "read_item", ex);
            throw Dependency(ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListActiveTagsAsync(string userId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT c.tags
                FROM c
                WHERE c.userId = @userId
                  AND c.active = true
                  AND (NOT IS_DEFINED(c.deletedAt) OR IS_NULL(c.deletedAt))
                  AND IS_DEFINED(c.tags)
                  AND IS_ARRAY(c.tags)
                  AND ARRAY_LENGTH(c.tags) > 0
                """)
            .WithParameter("@userId", userId);

        try
        {
            using var iterator = _container.GetItemQueryIterator<FixedRuleTagsProjection>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(userId),
                    MaxItemCount = 100
                });
            var tags = new HashSet<string>(StringComparer.Ordinal);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                _diagnostics.RecordCosmosOperation("fixedRules", "list_active_tags", page.RequestCharge, throttled: false);
                foreach (var item in page.Resource)
                {
                    foreach (var tag in item.Tags ?? [])
                    {
                        var normalized = (tag ?? "").Trim().ToLowerInvariant();
                        if (normalized.Length > 0)
                        {
                            tags.Add(normalized);
                        }
                    }
                }
            }

            return tags.ToArray();
        }
        catch (CosmosException ex)
        {
            RecordFailure("fixedRules", "list_active_tags", ex);
            throw Dependency(ex);
        }
    }

    private void RecordFailure(string container, string operation, CosmosException ex) =>
        _diagnostics.RecordCosmosOperation(container, operation, ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);

    private static TransactionsDependencyException Dependency(Exception ex) =>
        new("transactions_dependency_unavailable", "Transactions dependency unavailable.", ex);

    private sealed class FixedRuleTagsProjection
    {
        public string[] Tags { get; set; } = [];
    }
}

public sealed class PartnershipsReadRepository : IPartnershipsReadRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public PartnershipsReadRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.PartnershipsContainer);
        _diagnostics = diagnostics;
    }

    public async Task<PartnershipDocument?> GetActivePartnerAsync(string userId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                """
                SELECT * FROM c
                WHERE c.userId = @userId
                  AND c.status = "active"
                """)
            .WithParameter("@userId", userId);

        try
        {
            using var iterator = _container.GetItemQueryIterator<PartnershipDocument>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(userId),
                    MaxItemCount = 1
                });
            if (!iterator.HasMoreResults)
            {
                return null;
            }

            var page = await iterator.ReadNextAsync(cancellationToken);
            _diagnostics.RecordCosmosOperation("partnerships", "get_active_partner", page.RequestCharge, throttled: false);
            return page.Resource.FirstOrDefault();
        }
        catch (CosmosException ex)
        {
            RecordFailure("partnerships", "get_active_partner", ex);
            throw Dependency(ex);
        }
    }

    public async Task<bool> IsActivePartnerAsync(string userId, string partnerUserId, CancellationToken cancellationToken)
    {
        var partner = await GetActivePartnerAsync(userId, cancellationToken);
        return partner is not null && string.Equals(partner.PartnerUserId, partnerUserId, StringComparison.Ordinal);
    }

    private void RecordFailure(string container, string operation, CosmosException ex) =>
        _diagnostics.RecordCosmosOperation(container, operation, ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);

    private static TransactionsDependencyException Dependency(Exception ex) =>
        new("transactions_dependency_unavailable", "Transactions dependency unavailable.", ex);
}
