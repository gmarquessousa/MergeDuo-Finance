using Azure.Identity;
using MergeDuo.Partnership.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Partnership.Infra.Cosmos;

public static class CosmosClientFactory
{
    public static CosmosClient Create(CosmosOptions options)
    {
        var clientOptions = new CosmosClientOptions
        {
            ApplicationName = "mergeduo-partnership",
            ConsistencyLevel = ConsistencyLevel.Session,
            ConnectionMode = ConnectionMode.Direct,
            EnableContentResponseOnWrite = false,
            MaxRetryAttemptsOnRateLimitedRequests = 3,
            Serializer = new SystemTextJsonCosmosSerializer()
        };

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new CosmosClient(options.ConnectionString, clientOptions);
        }

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return new CosmosClient(options.Endpoint, new DefaultAzureCredential(), clientOptions);
        }

        throw new InvalidOperationException("Cosmos:Endpoint or Cosmos:ConnectionString must be configured.");
    }
}
