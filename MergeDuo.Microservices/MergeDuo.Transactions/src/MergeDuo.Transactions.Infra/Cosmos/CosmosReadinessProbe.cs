using MergeDuo.Transactions.Domain.Abstractions;
using MergeDuo.Transactions.Domain.Options;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Transactions.Infra.Cosmos;

public sealed class CosmosReadinessProbe(CosmosClient client, CosmosOptions options) : ITransactionsReadinessProbe
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        var database = client.GetDatabase(options.Database);
        await database.ReadAsync(cancellationToken: cancellationToken);
        return true;
    }
}
