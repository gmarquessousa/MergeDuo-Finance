using Azure.Identity;
using MergeDuo.Cards.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Cards.Infra.Cosmos;

public static class CosmosClientFactory
{
    public static CosmosClient Create(CosmosOptions options)
    {
        var clientOptions = new CosmosClientOptions
        {
            ApplicationName = "mergeduo-cards",
            ConsistencyLevel = ConsistencyLevel.Session,
            ConnectionMode = ConnectionMode.Direct,
            EnableContentResponseOnWrite = false,
            MaxRetryAttemptsOnRateLimitedRequests = 9,
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
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
