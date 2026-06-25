using System.Diagnostics.Metrics;
using MergeDuo.FixedRules.Domain.Abstractions;

namespace MergeDuo.FixedRules.Api;

public sealed class FixedRulesMetrics : ICosmosDiagnosticsRecorder
{
    public const string MeterName = "MergeDuo.FixedRules";

    private readonly Counter<long> _listRequested;
    private readonly Counter<long> _getRequested;
    private readonly Counter<long> _created;
    private readonly Counter<long> _updated;
    private readonly Counter<long> _paused;
    private readonly Counter<long> _resumed;
    private readonly Counter<long> _deleted;
    private readonly Counter<long> _previewRequested;
    private readonly Counter<long> _validationFailed;
    private readonly Counter<long> _cardValidationFailed;
    private readonly Counter<double> _cosmosRequestCharge;
    private readonly Counter<long> _cosmosThrottled;

    public FixedRulesMetrics()
    {
        var meter = new Meter(MeterName);
        _listRequested = meter.CreateCounter<long>("fixed_rules.list.requested");
        _getRequested = meter.CreateCounter<long>("fixed_rules.get.requested");
        _created = meter.CreateCounter<long>("fixed_rules.created");
        _updated = meter.CreateCounter<long>("fixed_rules.updated");
        _paused = meter.CreateCounter<long>("fixed_rules.paused");
        _resumed = meter.CreateCounter<long>("fixed_rules.resumed");
        _deleted = meter.CreateCounter<long>("fixed_rules.deleted");
        _previewRequested = meter.CreateCounter<long>("fixed_rules.preview.requested");
        _validationFailed = meter.CreateCounter<long>("fixed_rules.validation.failed");
        _cardValidationFailed = meter.CreateCounter<long>("fixed_rules.card_validation.failed");
        _cosmosRequestCharge = meter.CreateCounter<double>("fixed_rules.cosmos.request_charge");
        _cosmosThrottled = meter.CreateCounter<long>("fixed_rules.cosmos.throttled");
    }

    public void ListRequested(int statusCode, string reason) => AddStatus(_listRequested, statusCode, reason);
    public void GetRequested(int statusCode, string reason) => AddStatus(_getRequested, statusCode, reason);
    public void Created(int statusCode, string reason) => AddStatus(_created, statusCode, reason);
    public void PreviewRequested(int statusCode, string reason) => AddStatus(_previewRequested, statusCode, reason);
    public void ValidationFailed(string reason) => _validationFailed.Add(1, new KeyValuePair<string, object?>("failure_reason", reason));
    public void CardValidationFailed(string reason) => _cardValidationFailed.Add(1, new KeyValuePair<string, object?>("failure_reason", reason));

    public void Updated(int statusCode, string reason, bool hasIfMatch) => AddWrite(_updated, statusCode, reason, hasIfMatch);
    public void Paused(int statusCode, string reason, bool hasIfMatch) => AddWrite(_paused, statusCode, reason, hasIfMatch);
    public void Resumed(int statusCode, string reason, bool hasIfMatch) => AddWrite(_resumed, statusCode, reason, hasIfMatch);
    public void Deleted(int statusCode, string reason, bool hasIfMatch) => AddWrite(_deleted, statusCode, reason, hasIfMatch);

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

    private static void AddWrite(Counter<long> counter, int statusCode, string reason, bool hasIfMatch) =>
        counter.Add(
            1,
            new KeyValuePair<string, object?>("status_code", statusCode),
            new KeyValuePair<string, object?>("failure_reason", reason),
            new KeyValuePair<string, object?>("has_if_match", hasIfMatch));
}
