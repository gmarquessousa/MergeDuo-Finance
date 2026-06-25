using MergeDuo.Cards.Domain.Abstractions;

namespace MergeDuo.Cards.Tests.Fakes;

public sealed class FakeCardIdGenerator(params string[] ids) : ICardIdGenerator
{
    private readonly Queue<string> _ids = new(ids.Length == 0 ? ["card_test_01"] : ids);

    public string NewId() => _ids.Count == 0 ? "card_test_fallback" : _ids.Dequeue();
}
