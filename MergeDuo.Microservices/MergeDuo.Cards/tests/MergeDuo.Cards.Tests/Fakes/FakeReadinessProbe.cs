using MergeDuo.Cards.Domain.Abstractions;

namespace MergeDuo.Cards.Tests.Fakes;

public sealed class FakeReadinessProbe : ICardsReadinessProbe
{
    public bool Ready { get; set; } = true;

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(Ready);
}
