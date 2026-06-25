namespace MergeDuo.Profile.Tests.Support;

public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Set(DateTimeOffset now) => _now = now;
}
