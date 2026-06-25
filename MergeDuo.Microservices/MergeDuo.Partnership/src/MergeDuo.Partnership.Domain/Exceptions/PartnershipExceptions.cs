namespace MergeDuo.Partnership.Domain.Exceptions;

public abstract class PartnershipException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class PartnershipBadRequestException(string code, string message) : PartnershipException(code, message);

public sealed class PartnershipAccessException(string code, string message) : PartnershipException(code, message);

public sealed class PartnershipNotFoundException(string code, string message) : PartnershipException(code, message);

public sealed class PartnershipConflictException(string code, string message) : PartnershipException(code, message);

public sealed class PartnershipGoneException(string code, string message) : PartnershipException(code, message);

public sealed class PartnershipDependencyException(string code, string message, Exception? innerException = null)
    : PartnershipException(code, message)
{
    public Exception? InnerDependencyException { get; } = innerException;
}

public sealed class PartnershipThrottledException(string code, string message, TimeSpan? retryAfter = null, Exception? innerException = null)
    : PartnershipException(code, message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public Exception? InnerDependencyException { get; } = innerException;
}
