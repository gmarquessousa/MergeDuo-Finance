using MergeDuo.Aggregates.Domain.Abstractions;
using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Options;
using MergeDuo.Aggregates.Domain.Rules;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Aggregates.Api;

public sealed class FixedRulesChangeFeedHostedService(
    CosmosClient client,
    CosmosOptions cosmosOptions,
    ChangeFeedOptions changeFeedOptions,
    IAggregateRecomputeService recompute,
    AggregatesMetrics metrics,
    ILogger<FixedRulesChangeFeedHostedService> logger) : IHostedService
{
    private ChangeFeedProcessor? _processor;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!changeFeedOptions.Enabled)
        {
            logger.LogInformation("Aggregates fixed rules Change Feed processor disabled by configuration.");
            return;
        }

        var monitored = client.GetContainer(cosmosOptions.Database, cosmosOptions.FixedRulesContainer);
        var leases = client.GetContainer(cosmosOptions.Database, cosmosOptions.LeaseContainer);
        _processor = monitored
            .GetChangeFeedProcessorBuilder<FixedRuleDocument>(
                $"{changeFeedOptions.ProcessorName}-fixed-rules",
                HandleChangesAsync)
            .WithInstanceName($"{changeFeedOptions.InstanceName}-fixed-rules")
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

    private async Task HandleChangesAsync(IReadOnlyCollection<FixedRuleDocument> changes, CancellationToken cancellationToken)
    {
        var affected = new Dictionary<string, FixedRuleDocument>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (!string.Equals(change.DocType, "fixedRule", StringComparison.Ordinal))
            {
                continue;
            }

            if (!UserIdRules.IsValid(change.UserId))
            {
                metrics.ChangeFeedBatchFailed("invalid_fixed_rule_projection");
                throw new InvalidOperationException("Invalid fixed rule projection in Change Feed.");
            }

            affected[change.UserId + "|" + change.Id] = change;
        }

        foreach (var fixedRule in affected.Values)
        {
            try
            {
                metrics.RecomputeStarted();
                await recompute.RecomputeForFixedRuleChangeAsync(fixedRule, cancellationToken);
                metrics.RecomputeCompleted();
            }
            catch (Exception ex)
            {
                metrics.RecomputeFailed(ex.GetType().Name);
                logger.LogError(
                    ex,
                    "Failed to recompute aggregates for fixed rule {FixedRuleId}.",
                    fixedRule.Id);
                throw;
            }
        }
    }
}
