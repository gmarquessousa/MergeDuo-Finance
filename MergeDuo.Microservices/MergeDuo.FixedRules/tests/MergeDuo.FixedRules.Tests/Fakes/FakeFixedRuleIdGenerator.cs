using MergeDuo.FixedRules.Domain.Abstractions;

namespace MergeDuo.FixedRules.Tests.Fakes;

public sealed class FakeFixedRuleIdGenerator(params string[] ids) : IFixedRuleIdGenerator
{
    private readonly Queue<string> _ids = new(ids);

    public string NewId() => _ids.Count > 0 ? _ids.Dequeue() : "fxr_test_fallback";
}
