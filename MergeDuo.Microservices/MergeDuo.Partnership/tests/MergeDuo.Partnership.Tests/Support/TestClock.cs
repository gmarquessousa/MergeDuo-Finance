namespace MergeDuo.Partnership.Tests.Support;

public sealed class TestClock(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; private set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
}
