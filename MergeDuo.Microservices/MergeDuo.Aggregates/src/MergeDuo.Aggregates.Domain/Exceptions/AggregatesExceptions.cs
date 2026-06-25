namespace MergeDuo.Aggregates.Domain.Exceptions;

public abstract class AggregatesException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public sealed class AggregatesBadRequestException(string code, string message)
    : AggregatesException(code, message);

public sealed class AggregatesForbiddenException(string code, string message)
    : AggregatesException(code, message);

public sealed class AggregatesConflictException(string code, string message)
    : AggregatesException(code, message);

public sealed class AggregatesDependencyException(string code, string message, Exception? innerException = null)
    : AggregatesException(code, message, innerException);

public sealed class InvalidTransactionProjectionException(string message)
    : AggregatesException("invalid_transaction_projection", message);
