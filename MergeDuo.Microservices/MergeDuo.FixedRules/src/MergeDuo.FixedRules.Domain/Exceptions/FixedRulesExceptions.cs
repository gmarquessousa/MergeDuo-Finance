namespace MergeDuo.FixedRules.Domain.Exceptions;

public abstract class FixedRulesException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class FixedRulesBadRequestException(string code, string message) : FixedRulesException(code, message);

public sealed class FixedRulesNotFoundException(string code, string message) : FixedRulesException(code, message);

public sealed class FixedRulesConflictException(string code, string message) : FixedRulesException(code, message);

public sealed class FixedRulesPreconditionFailedException(string code, string message) : FixedRulesException(code, message);

public sealed class FixedRulesDependencyException(string code, string message, Exception? innerException = null)
    : FixedRulesException(code, message)
{
    public Exception? InnerDependencyException { get; } = innerException;
}
