using System.Diagnostics.Metrics;
using MergeDuo.Transactions.Domain.Abstractions;

namespace MergeDuo.Transactions.Api;

public sealed class TransactionsMetrics : ICosmosDiagnosticsRecorder
{
    public const string MeterName = "MergeDuo.Transactions";

    private readonly Counter<long> _listRequested;
    private readonly Counter<long> _getRequested;
    private readonly Counter<long> _created;
    private readonly Counter<long> _updated;
    private readonly Counter<long> _deleted;
    private readonly Counter<long> _groupDeleted;
    private readonly Counter<long> _cardUsageRequested;
    private readonly Counter<long> _validationFailed;
    private readonly Counter<double> _cosmosRequestCharge;
    private readonly Counter<long> _cosmosThrottled;

    public TransactionsMetrics()
    {
        var meter = new Meter(MeterName);
        _listRequested = meter.CreateCounter<long>("transactions.list.requested");
        _getRequested = meter.CreateCounter<long>("transactions.get.requested");
        _created = meter.CreateCounter<long>("transactions.created");
        _updated = meter.CreateCounter<long>("transactions.updated");
        _deleted = meter.CreateCounter<long>("transactions.deleted");
        _groupDeleted = meter.CreateCounter<long>("transactions.group.deleted");
        _cardUsageRequested = meter.CreateCounter<long>("transactions.card_usage.requested");
        _validationFailed = meter.CreateCounter<long>("transactions.validation.failed");
        _cosmosRequestCharge = meter.CreateCounter<double>("transactions.cosmos.request_charge");
        _cosmosThrottled = meter.CreateCounter<long>("transactions.cosmos.throttled");
    }

    public void ListRequested(int statusCode, string reason) => AddStatus(_listRequested, statusCode, reason);
    public void GetRequested(int statusCode, string reason) => AddStatus(_getRequested, statusCode, reason);
    public void Created(int statusCode, string reason) => AddStatus(_created, statusCode, reason);
    public void Updated(int statusCode, string reason, bool hasIfMatch) => AddStatus(_updated, statusCode, reason, hasIfMatch);
    public void Deleted(int statusCode, string reason, bool hasIfMatch) => AddStatus(_deleted, statusCode, reason, hasIfMatch);
    public void GroupDeleted(int statusCode, string reason) => AddStatus(_groupDeleted, statusCode, reason);
    public void CardUsageRequested(int statusCode, string reason) => AddStatus(_cardUsageRequested, statusCode, reason);
    public void ValidationFailed(string reason) => _validationFailed.Add(1, new KeyValuePair<string, object?>("failure_reason", reason));

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

    private static void AddStatus(Counter<long> counter, int statusCode, string reason, bool? hasIfMatch = null)
    {
        if (hasIfMatch is null)
        {
            counter.Add(
                1,
                new KeyValuePair<string, object?>("status_code", statusCode),
                new KeyValuePair<string, object?>("failure_reason", reason));
            return;
        }

        counter.Add(
            1,
            new KeyValuePair<string, object?>("status_code", statusCode),
            new KeyValuePair<string, object?>("failure_reason", reason),
            new KeyValuePair<string, object?>("has_if_match", hasIfMatch.Value));
    }
}
