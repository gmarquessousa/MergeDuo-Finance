using Azure.Identity;
using MergeDuo.Transactions.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Transactions.Infra.Cosmos;

public static class CosmosClientFactory
{
    public static CosmosClient Create(CosmosOptions options)
    {
        var clientOptions = new CosmosClientOptions
        {
            ApplicationName = "mergeduo-transactions",
            ConsistencyLevel = ConsistencyLevel.Session,
            ConnectionMode = ConnectionMode.Direct,
            EnableContentResponseOnWrite = false,
            MaxRetryAttemptsOnRateLimitedRequests = 9,
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        };

        return string.IsNullOrWhiteSpace(options.ConnectionString)
            ? new CosmosClient(options.Endpoint, new DefaultAzureCredential(), clientOptions)
            : new CosmosClient(options.ConnectionString, clientOptions);
    }
}
