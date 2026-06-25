using System.Diagnostics.Metrics;
using MergeDuo.Aggregates.Domain.Abstractions;

namespace MergeDuo.Aggregates.Api;

public sealed class AggregatesMetrics : ICosmosDiagnosticsRecorder
{
    public const string MeterName = "MergeDuo.Aggregates";

    private readonly Counter<long> _monthRequested;
    private readonly Counter<long> _yearRequested;
    private readonly Counter<long> _zeroFilled;
    private readonly Counter<long> _staleReturned;
    private readonly Counter<long> _changeFeedBatchReceived;
    private readonly Counter<long> _changeFeedBatchFailed;
    private readonly Counter<long> _recomputeStarted;
    private readonly Counter<long> _recomputeCompleted;
    private readonly Counter<long> _recomputeFailed;
    private readonly Counter<double> _cosmosRequestCharge;
    private readonly Counter<long> _cosmosThrottled;

    public AggregatesMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _monthRequested = meter.CreateCounter<long>("aggregates.month.requested");
        _yearRequested = meter.CreateCounter<long>("aggregates.year.requested");
        _zeroFilled = meter.CreateCounter<long>("aggregates.zero_filled");
        _staleReturned = meter.CreateCounter<long>("aggregates.stale_returned");
        _changeFeedBatchReceived = meter.CreateCounter<long>("aggregates.changefeed.batch_received");
        _changeFeedBatchFailed = meter.CreateCounter<long>("aggregates.changefeed.batch_failed");
        _recomputeStarted = meter.CreateCounter<long>("aggregates.recompute.started");
        _recomputeCompleted = meter.CreateCounter<long>("aggregates.recompute.completed");
        _recomputeFailed = meter.CreateCounter<long>("aggregates.recompute.failed");
        _cosmosRequestCharge = meter.CreateCounter<double>("aggregates.cosmos.request_charge");
        _cosmosThrottled = meter.CreateCounter<long>("aggregates.cosmos.throttled");
    }

    public void MonthRequested(int statusCode, string reason) =>
        _monthRequested.Add(1, Tags(statusCode, reason));

    public void YearRequested(int statusCode, string reason) =>
        _yearRequested.Add(1, Tags(statusCode, reason));

    public void ResponseShape(string source, bool isStale)
    {
        if (source == "empty")
        {
            _zeroFilled.Add(1);
        }

        if (isStale)
        {
            _staleReturned.Add(1);
        }
    }

    public void ChangeFeedBatchReceived(int size) =>
        _changeFeedBatchReceived.Add(1, new KeyValuePair<string, object?>("batch_size", size));

    public void ChangeFeedBatchFailed(string reason) =>
        _changeFeedBatchFailed.Add(1, new KeyValuePair<string, object?>("failure_reason", reason));

    public void RecomputeStarted() => _recomputeStarted.Add(1);
    public void RecomputeCompleted() => _recomputeCompleted.Add(1);
    public void RecomputeFailed(string reason) => _recomputeFailed.Add(1, new KeyValuePair<string, object?>("failure_reason", reason));

    public void RecordCosmosOperation(string container, string operation, double requestCharge, bool throttled)
    {
        _cosmosRequestCharge.Add(
            requestCharge,
            new KeyValuePair<string, object?>("cosmos_container", container),
            new KeyValuePair<string, object?>("operation", operation));
        if (throttled)
        {
            _cosmosThrottled.Add(1, new KeyValuePair<string, object?>("cosmos_container", container));
        }
    }

    private static KeyValuePair<string, object?>[] Tags(int statusCode, string reason) =>
    [
        new("status_code", statusCode),
        new("failure_reason", reason)
    ];
}
