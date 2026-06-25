using System.Text.RegularExpressions;

namespace MergeDuo.Profile.Domain.Rules;

public static partial class UserIdRules
{
    public static bool IsValid(string? userId) =>
        userId is not null && UserIdRegex().IsMatch(userId);

    [GeneratedRegex("^usr_[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UserIdRegex();
}
