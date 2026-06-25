using System.Net;
using MergeDuo.FixedRules.Domain.Abstractions;
using MergeDuo.FixedRules.Domain.Documents;
using MergeDuo.FixedRules.Domain.Exceptions;
using MergeDuo.FixedRules.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.FixedRules.Infra.Cosmos;

public sealed class CardsReadRepository : ICardsReadRepository
{
    private readonly Container _container;
    private readonly ICosmosDiagnosticsRecorder _diagnostics;

    public CardsReadRepository(CosmosClient client, CosmosOptions options, ICosmosDiagnosticsRecorder diagnostics)
    {
        _container = client.GetContainer(options.Database, options.CardsContainer);
        _diagnostics = diagnostics;
    }

    public async Task<CardProjection?> GetActiveCardAsync(
        string userId,
        string cardId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<CardProjection>(
                cardId,
                new PartitionKey(userId),
                cancellationToken: cancellationToken);
            _diagnostics.RecordCosmosOperation("cards", "read_item", response.RequestCharge, throttled: false);

            return response.Resource.DeletedAt is null ? response.Resource : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            _diagnostics.RecordCosmosOperation("cards", "read_item", ex.RequestCharge, ex.StatusCode == (HttpStatusCode)429);
            throw new FixedRulesDependencyException("fixed_rules_dependency_unavailable", "FixedRules dependency unavailable.", ex);
        }
    }
}
