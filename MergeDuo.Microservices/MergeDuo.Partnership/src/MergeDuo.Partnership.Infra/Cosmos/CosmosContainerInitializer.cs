using MergeDuo.Partnership.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Partnership.Infra.Cosmos;

public static class CosmosContainerInitializer
{
    public static async Task EnsureCreatedAsync(
        CosmosClient client,
        CosmosOptions options,
        CancellationToken cancellationToken)
    {
        var databaseResponse = await client.CreateDatabaseIfNotExistsAsync(
            options.Database,
            cancellationToken: cancellationToken);

        var database = databaseResponse.Database;

        var invitesProperties = new ContainerProperties(options.InvitesContainer, "/inviterUserId")
        {
            DefaultTimeToLive = -1
        };
        invitesProperties.UniqueKeyPolicy.UniqueKeys.Add(new UniqueKey
        {
            Paths = { "/token" }
        });
        invitesProperties.IndexingPolicy.CompositeIndexes.Add(new System.Collections.ObjectModel.Collection<CompositePath>
        {
            new() { Path = "/inviterUserId", Order = CompositePathSortOrder.Ascending },
            new() { Path = "/status", Order = CompositePathSortOrder.Ascending },
            new() { Path = "/createdAt", Order = CompositePathSortOrder.Descending }
        });

        await database.CreateContainerIfNotExistsAsync(invitesProperties, cancellationToken: cancellationToken);

        var partnershipsProperties = new ContainerProperties(options.PartnershipsContainer, "/userId");
        partnershipsProperties.IndexingPolicy.CompositeIndexes.Add(new System.Collections.ObjectModel.Collection<CompositePath>
        {
            new() { Path = "/userId", Order = CompositePathSortOrder.Ascending },
            new() { Path = "/status", Order = CompositePathSortOrder.Ascending },
            new() { Path = "/mergedSince", Order = CompositePathSortOrder.Descending }
        });

        await database.CreateContainerIfNotExistsAsync(partnershipsProperties, cancellationToken: cancellationToken);
    }
}
