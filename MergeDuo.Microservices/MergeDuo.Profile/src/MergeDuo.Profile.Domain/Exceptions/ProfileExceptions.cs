namespace MergeDuo.Profile.Domain.Exceptions;

public sealed class ProfileConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ProfileNotFoundException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ProfileAccessException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ProfileDependencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);
