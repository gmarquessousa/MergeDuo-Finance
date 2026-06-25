using MergeDuo.Aggregates.Domain.Abstractions;
using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Options;
using MergeDuo.Aggregates.Domain.Rules;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Aggregates.Api;

public sealed class TransactionsChangeFeedHostedService(
    CosmosClient client,
    CosmosOptions cosmosOptions,
    ChangeFeedOptions changeFeedOptions,
    IAggregateRecomputeService recompute,
    AggregatesMetrics metrics,
    ILogger<TransactionsChangeFeedHostedService> logger) : IHostedService
{
    private ChangeFeedProcessor? _processor;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!changeFeedOptions.Enabled)
        {
            logger.LogInformation("Aggregates Change Feed processor disabled by configuration.");
            return;
        }

        var monitored = client.GetContainer(cosmosOptions.Database, cosmosOptions.TransactionsContainer);
        var leases = client.GetContainer(cosmosOptions.Database, cosmosOptions.LeaseContainer);
        _processor = monitored
            .GetChangeFeedProcessorBuilder<TransactionProjection>(
                changeFeedOptions.ProcessorName,
                HandleChangesAsync)
            .WithInstanceName(changeFeedOptions.InstanceName)
            .WithLeaseContainer(leases)
            .WithMaxItems(changeFeedOptions.MaxItemsPerInvocation)
            .Build();

        await _processor.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopAsync();
        }
    }

    private async Task HandleChangesAsync(IReadOnlyCollection<TransactionProjection> changes, CancellationToken cancellationToken)
    {
        metrics.ChangeFeedBatchReceived(changes.Count);
        var affected = new HashSet<(string UserId, YearMonth YearMonth)>();

        foreach (var change in changes)
        {
            if (!string.Equals(change.DocType, "transaction", StringComparison.Ordinal))
            {
                continue;
            }

            if (!UserIdRules.IsValid(change.UserId) || !YearMonth.TryParse(change.YearMonth, out var yearMonth))
            {
                metrics.ChangeFeedBatchFailed("invalid_transaction_projection");
                throw new InvalidOperationException("Invalid transaction projection in Change Feed.");
            }

            affected.Add((change.UserId, yearMonth));
        }

        foreach (var group in affected)
        {
            try
            {
                metrics.RecomputeStarted();
                await recompute.RecomputeForChangeAsync(group.UserId, group.YearMonth, cancellationToken);
                metrics.RecomputeCompleted();
            }
            catch (Exception ex)
            {
                metrics.RecomputeFailed(ex.GetType().Name);
                logger.LogError(
                    ex,
                    "Failed to recompute aggregate for user {UserId} and month {YearMonth}.",
                    group.UserId,
                    group.YearMonth.Value);
                throw;
            }
        }
    }
}
