using System.Diagnostics.Metrics;
using MergeDuo.Cards.Domain.Abstractions;

namespace MergeDuo.Cards.Api;

public sealed class CardsMetrics : ICosmosDiagnosticsRecorder
{
    public const string MeterName = "MergeDuo.Cards";

    private readonly Counter<long> _listRequested;
    private readonly Counter<long> _getRequested;
    private readonly Counter<long> _created;
    private readonly Counter<long> _updated;
    private readonly Counter<long> _deleted;
    private readonly Counter<long> _usageRequested;
    private readonly Counter<long> _usageDependencyFailed;
    private readonly Counter<long> _validationFailed;
    private readonly Counter<double> _cosmosRequestCharge;
    private readonly Counter<long> _cosmosThrottled;

    public CardsMetrics()
    {
        var meter = new Meter(MeterName);
        _listRequested = meter.CreateCounter<long>("cards.list.requested");
        _getRequested = meter.CreateCounter<long>("cards.get.requested");
        _created = meter.CreateCounter<long>("cards.created");
        _updated = meter.CreateCounter<long>("cards.updated");
        _deleted = meter.CreateCounter<long>("cards.deleted");
        _usageRequested = meter.CreateCounter<long>("cards.usage.requested");
        _usageDependencyFailed = meter.CreateCounter<long>("cards.usage.dependency_failed");
        _validationFailed = meter.CreateCounter<long>("cards.validation.failed");
        _cosmosRequestCharge = meter.CreateCounter<double>("cards.cosmos.request_charge");
        _cosmosThrottled = meter.CreateCounter<long>("cards.cosmos.throttled");
    }

    public void ListRequested(int statusCode, string reason) =>
        AddStatus(_listRequested, statusCode, reason);

    public void GetRequested(int statusCode, string reason) =>
        AddStatus(_getRequested, statusCode, reason);

    public void Created(int statusCode, string reason) =>
        AddStatus(_created, statusCode, reason);

    public void Updated(int statusCode, string reason, bool hasIfMatch) =>
        _updated.Add(
            1,
            new KeyValuePair<string, object?>("status_code", statusCode),
            new KeyValuePair<string, object?>("failure_reason", reason),
            new KeyValuePair<string, object?>("has_if_match", hasIfMatch));

    public void Deleted(int statusCode, string reason, bool hasIfMatch) =>
        _deleted.Add(
            1,
            new KeyValuePair<string, object?>("status_code", statusCode),
            new KeyValuePair<string, object?>("failure_reason", reason),
            new KeyValuePair<string, object?>("has_if_match", hasIfMatch));

    public void UsageRequested(int statusCode, string reason) =>
        AddStatus(_usageRequested, statusCode, reason);

    public void UsageDependencyFailed() =>
        _usageDependencyFailed.Add(1, new KeyValuePair<string, object?>("dependency", "transactions-service"));

    public void ValidationFailed(string reason) =>
        _validationFailed.Add(1, new KeyValuePair<string, object?>("failure_reason", reason));

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

    private static void AddStatus(Counter<long> counter, int statusCode, string reason) =>
        counter.Add(
            1,
            new KeyValuePair<string, object?>("status_code", statusCode),
            new KeyValuePair<string, object?>("failure_reason", reason));
}
