using System.Diagnostics.Metrics;

namespace MergeDuo.Identity.Api;

public sealed class IdentityMetrics
{
    public const string MeterName = "MergeDuo.Identity";
    private readonly Counter<long> _loginSuccess;
    private readonly Counter<long> _loginFailure;
    private readonly Counter<long> _refreshSuccess;
    private readonly Counter<long> _refreshFailure;
    private readonly Counter<long> _logoutSuccess;
    private readonly Counter<long> _avatarUploaded;
    private readonly Counter<long> _userDeleted;
    private readonly Histogram<double> _loginDurationMs;

    public IdentityMetrics()
    {
        var meter = new Meter(MeterName);
        _loginSuccess = meter.CreateCounter<long>("auth.login.success");
        _loginFailure = meter.CreateCounter<long>("auth.login.failure");
        _refreshSuccess = meter.CreateCounter<long>("auth.refresh.success");
        _refreshFailure = meter.CreateCounter<long>("auth.refresh.failure");
        _logoutSuccess = meter.CreateCounter<long>("auth.logout.success");
        _avatarUploaded = meter.CreateCounter<long>("identity.avatar.uploaded");
        _userDeleted = meter.CreateCounter<long>("identity.user.deleted");
        _loginDurationMs = meter.CreateHistogram<double>("auth.login.duration.ms", unit: "ms");
    }

    public void LoginSuccess() => _loginSuccess.Add(1);
    public void LoginFailure(string reason) => _loginFailure.Add(1, new KeyValuePair<string, object?>("failure_reason", reason));
    public void LoginDuration(TimeSpan duration, string result, string? reason)
    {
        var tags = reason is null
            ? new KeyValuePair<string, object?>[] { new("result", result) }
            : [new("result", result), new("failure_reason", reason)];
        _loginDurationMs.Record(duration.TotalMilliseconds, tags);
    }
    public void RefreshSuccess() => _refreshSuccess.Add(1);
    public void RefreshFailure(string reason) => _refreshFailure.Add(1, new KeyValuePair<string, object?>("failure_reason", reason));
    public void LogoutSuccess() => _logoutSuccess.Add(1);
    public void AvatarUploaded() => _avatarUploaded.Add(1);
    public void UserDeleted() => _userDeleted.Add(1);
}
