using MergeDuo.Partnership.Domain.Abstractions;
using MergeDuo.Partnership.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Partnership.Infra.Cosmos;

public sealed class CosmosReadinessProbe(CosmosClient client, CosmosOptions options) : IPartnershipReadinessProbe
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        var container = client.GetContainer(options.Database, options.InvitesContainer);
        await container.ReadContainerAsync(cancellationToken: cancellationToken);
        return true;
    }
}
