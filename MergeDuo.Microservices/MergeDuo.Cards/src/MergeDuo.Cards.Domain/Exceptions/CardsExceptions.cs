namespace MergeDuo.Cards.Domain.Exceptions;

public abstract class CardsException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class CardsBadRequestException(string code, string message) : CardsException(code, message);

public sealed class CardsNotFoundException(string code, string message) : CardsException(code, message);

public sealed class CardsConflictException(string code, string message) : CardsException(code, message);

public sealed class CardsPreconditionFailedException(string code, string message) : CardsException(code, message);

public sealed class CardsDependencyException(string code, string message, Exception? innerException = null)
    : CardsException(code, message)
{
    public Exception? InnerDependencyException { get; } = innerException;
}
