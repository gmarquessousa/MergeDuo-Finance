using MergeDuo.Identity.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Identity.Infra.Cosmos;

public static class CosmosContainerInitializer
{
    public static async Task EnsureCreatedAsync(
        CosmosClient client,
        CosmosOptions options,
        CancellationToken cancellationToken)
    {
        var databaseResponse = await client.CreateDatabaseIfNotExistsAsync(
            options.Database,
            options.DatabaseThroughput,
            cancellationToken: cancellationToken);

        var database = databaseResponse.Database;

        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(options.UsersContainer, "/id"),
            cancellationToken: cancellationToken);

        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(options.DevicesContainer, "/userId")
            {
                DefaultTimeToLive = -1
            },
            cancellationToken: cancellationToken);

        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(options.IdentityReservationsContainer, "/id")
            {
                DefaultTimeToLive = -1
            },
            cancellationToken: cancellationToken);
    }
}
