using MergeDuo.Cards.Domain.Abstractions;
using MergeDuo.Cards.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Cards.Infra.Cosmos;

public sealed class CosmosReadinessProbe(CosmosClient client, CosmosOptions options) : ICardsReadinessProbe
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Database, options.CardsContainer);
        await container.ReadContainerAsync(cancellationToken: cancellationToken);
        return true;
    }
}
