using System.Diagnostics.Metrics;
using MergeDuo.Partnership.Domain.Abstractions;

namespace MergeDuo.Partnership.Api;

public sealed class PartnershipMetrics : ICosmosDiagnosticsRecorder
{
    public const string MeterName = "MergeDuo.Partnership";

    private readonly Counter<long> _inviteCreated;
    private readonly Counter<long> _invitePreviewed;
    private readonly Counter<long> _inviteAccepted;
    private readonly Counter<long> _inviteRevoked;
    private readonly Counter<long> _currentRequested;
    private readonly Counter<long> _paused;
    private readonly Counter<long> _ended;
    private readonly Counter<double> _cosmosRequestCharge;
    private readonly Counter<long> _cosmosThrottled;

    public PartnershipMetrics()
    {
        var meter = new Meter(MeterName);
        _inviteCreated = meter.CreateCounter<long>("partnership.invite.created");
        _invitePreviewed = meter.CreateCounter<long>("partnership.invite.previewed");
        _inviteAccepted = meter.CreateCounter<long>("partnership.invite.accepted");
        _inviteRevoked = meter.CreateCounter<long>("partnership.invite.revoked");
        _currentRequested = meter.CreateCounter<long>("partnership.current.requested");
        _paused = meter.CreateCounter<long>("partnership.paused");
        _ended = meter.CreateCounter<long>("partnership.ended");
        _cosmosRequestCharge = meter.CreateCounter<double>("partnership.cosmos.request_charge");
        _cosmosThrottled = meter.CreateCounter<long>("partnership.cosmos.throttled");
    }

    public void InviteCreated(int statusCode, string reason) => Add(_inviteCreated, statusCode, reason);

    public void InvitePreviewed(int statusCode, string reason) => Add(_invitePreviewed, statusCode, reason);

    public void InviteAccepted(int statusCode, string reason) => Add(_inviteAccepted, statusCode, reason);

    public void InviteRevoked(int statusCode, string reason) => Add(_inviteRevoked, statusCode, reason);

    public void CurrentRequested(int statusCode, string reason) => Add(_currentRequested, statusCode, reason);

    public void Paused(int statusCode, string reason) => Add(_paused, statusCode, reason);

    public void Ended(int statusCode, string reason) => Add(_ended, statusCode, reason);

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

    private static void Add(Counter<long> counter, int statusCode, string reason) =>
        counter.Add(
            1,
            new KeyValuePair<string, object?>("status_code", statusCode),
            new KeyValuePair<string, object?>("reason", reason));
}
