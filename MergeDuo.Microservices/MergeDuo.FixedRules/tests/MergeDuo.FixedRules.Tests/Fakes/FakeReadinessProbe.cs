using MergeDuo.FixedRules.Domain.Abstractions;

namespace MergeDuo.FixedRules.Tests.Fakes;

public sealed class FakeReadinessProbe : IFixedRulesReadinessProbe
{
    public bool Ready { get; set; } = true;

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(Ready);
}
