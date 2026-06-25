using MergeDuo.Profile.Domain.Abstractions;
using MergeDuo.Profile.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Profile.Infra.Cosmos;

public sealed class CosmosReadinessProbe(CosmosClient client, CosmosOptions options) : IProfileReadinessProbe
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Database, options.UsersContainer);
        await container.ReadContainerAsync(cancellationToken: cancellationToken);
        return true;
    }
}
