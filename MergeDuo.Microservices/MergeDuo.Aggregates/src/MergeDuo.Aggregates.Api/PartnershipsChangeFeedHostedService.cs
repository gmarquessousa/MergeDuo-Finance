using MergeDuo.Aggregates.Domain.Abstractions;
using MergeDuo.Aggregates.Domain.Documents;
using MergeDuo.Aggregates.Domain.Options;
using MergeDuo.Aggregates.Domain.Rules;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Aggregates.Api;

public sealed class PartnershipsChangeFeedHostedService(
    CosmosClient client,
    CosmosOptions cosmosOptions,
    ChangeFeedOptions changeFeedOptions,
    IAggregateRecomputeService recompute,
    AggregatesMetrics metrics,
    ILogger<PartnershipsChangeFeedHostedService> logger) : IHostedService
{
    private ChangeFeedProcessor? _processor;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!changeFeedOptions.Enabled)
        {
            logger.LogInformation("Aggregates partnership Change Feed processor disabled by configuration.");
            return;
        }

        var monitored = client.GetContainer(cosmosOptions.Database, cosmosOptions.PartnershipsContainer);
        var leases = client.GetContainer(cosmosOptions.Database, cosmosOptions.LeaseContainer);
        _processor = monitored
            .GetChangeFeedProcessorBuilder<PartnershipDocument>(
                $"{changeFeedOptions.ProcessorName}-partnerships",
                HandleChangesAsync)
            .WithInstanceName($"{changeFeedOptions.InstanceName}-partnerships")
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

    private async Task HandleChangesAsync(IReadOnlyCollection<PartnershipDocument> changes, CancellationToken cancellationToken)
    {
        var affected = new Dictionary<string, PartnershipDocument>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (!string.Equals(change.DocType, "partnership", StringComparison.Ordinal))
            {
                continue;
            }

            if (!UserIdRules.IsValid(change.UserId) ||
                !UserIdRules.IsValid(change.PartnerUserId) ||
                change.MergedSince == default)
            {
                metrics.ChangeFeedBatchFailed("invalid_partnership_projection");
                throw new InvalidOperationException("Invalid partnership projection in Change Feed.");
            }

            affected[change.PartnershipId + "|" + change.UserId] = change;
        }

        foreach (var partnership in affected.Values)
        {
            try
            {
                metrics.RecomputeStarted();
                await recompute.RecomputeForPartnershipChangeAsync(partnership, cancellationToken);
                metrics.RecomputeCompleted();
            }
            catch (Exception ex)
            {
                metrics.RecomputeFailed(ex.GetType().Name);
                logger.LogError(
                    ex,
                    "Failed to recompute aggregates for partnership {PartnershipId}.",
                    partnership.PartnershipId);
                throw;
            }
        }
    }
}
