using MergeDuo.Aggregates.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Aggregates.Infra.Cosmos;

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

        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(options.MonthlyAggregatesContainer, "/userId"),
            cancellationToken: cancellationToken);

        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(options.LeaseContainer, "/id"),
            cancellationToken: cancellationToken);
    }
}
