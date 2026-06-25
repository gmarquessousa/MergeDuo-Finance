namespace MergeDuo.Transactions.Domain.Exceptions;

public abstract class TransactionsException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class TransactionsBadRequestException(string code, string message)
    : TransactionsException(code, message);

public sealed class TransactionsNotFoundException(string code, string message)
    : TransactionsException(code, message);

public sealed class TransactionsForbiddenException(string code, string message)
    : TransactionsException(code, message);

public sealed class TransactionsConflictException(string code, string message)
    : TransactionsException(code, message);

public sealed class TransactionsPreconditionFailedException(string code, string message)
    : TransactionsException(code, message);

public sealed class TransactionsDependencyException(string code, string message, Exception? innerException = null)
    : TransactionsException(code, message)
{
    public Exception? InnerDependencyException { get; } = innerException;
}
