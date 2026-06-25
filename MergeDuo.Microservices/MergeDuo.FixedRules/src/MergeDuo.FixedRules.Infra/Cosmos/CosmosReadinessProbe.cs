using MergeDuo.FixedRules.Domain.Abstractions;
using MergeDuo.FixedRules.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.FixedRules.Infra.Cosmos;

public sealed class CosmosReadinessProbe : IFixedRulesReadinessProbe
{
    private readonly Container _container;

    public CosmosReadinessProbe(CosmosClient client, CosmosOptions options)
    {
        _container = client.GetContainer(options.Database, options.FixedRulesContainer);
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var iterator = _container.GetItemQueryIterator<int>(
                new QueryDefinition("SELECT VALUE 1"),
                requestOptions: new QueryRequestOptions { MaxItemCount = 1 });

            if (!iterator.HasMoreResults)
            {
                return true;
            }

            await iterator.ReadNextAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
