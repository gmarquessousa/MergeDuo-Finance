using System.Diagnostics.Metrics;
using MergeDuo.Profile.Domain.Abstractions;

namespace MergeDuo.Profile.Api;

public sealed class ProfileMetrics : ICosmosDiagnosticsRecorder
{
    public const string MeterName = "MergeDuo.Profile";

    private readonly Counter<long> _publicProfileRequested;
    private readonly Counter<long> _handleLookupRequested;
    private readonly Counter<long> _statsCacheHit;
    private readonly Counter<long> _statsCacheStale;
    private readonly Counter<long> _statsRecomputed;
    private readonly Counter<long> _statsRecomputeFailed;
    private readonly Counter<double> _cosmosRequestCharge;
    private readonly Counter<long> _cosmosThrottled;
    private readonly Counter<long> _relationshipFound;

    public ProfileMetrics()
    {
        var meter = new Meter(MeterName);
        _publicProfileRequested = meter.CreateCounter<long>("profile.public_profile.requested");
        _handleLookupRequested = meter.CreateCounter<long>("profile.handle_lookup.requested");
        _statsCacheHit = meter.CreateCounter<long>("profile.stats.cache_hit");
        _statsCacheStale = meter.CreateCounter<long>("profile.stats.cache_stale");
        _statsRecomputed = meter.CreateCounter<long>("profile.stats.recomputed");
        _statsRecomputeFailed = meter.CreateCounter<long>("profile.stats.recompute_failed");
        _cosmosRequestCharge = meter.CreateCounter<double>("profile.cosmos.request_charge");
        _cosmosThrottled = meter.CreateCounter<long>("profile.cosmos.throttled");
        _relationshipFound = meter.CreateCounter<long>("profile.relationship.found");
    }

    public void PublicProfileRequested(int statusCode, string reason) =>
        _publicProfileRequested.Add(
            1,
            new KeyValuePair<string, object?>("status_code", statusCode),
            new KeyValuePair<string, object?>("failure_reason", reason));

    public void HandleLookupRequested(int statusCode, string reason) =>
        _handleLookupRequested.Add(
            1,
            new KeyValuePair<string, object?>("status_code", statusCode),
            new KeyValuePair<string, object?>("failure_reason", reason));

    public void StatsResult(string source, bool isStale)
    {
        if (source == "recomputed")
        {
            _statsRecomputed.Add(1);
            return;
        }

        _statsCacheHit.Add(1);
        if (isStale)
        {
            _statsCacheStale.Add(1);
        }
    }

    public void StatsRecomputeFailed() => _statsRecomputeFailed.Add(1);

    public void RelationshipFound() => _relationshipFound.Add(1);

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
}
