using System.Text.RegularExpressions;

namespace MergeDuo.Partnership.Domain.Rules;

public static partial class UserIdRules
{
    public static bool IsValid(string? userId) =>
        !string.IsNullOrWhiteSpace(userId) && UserIdRegex().IsMatch(userId);

    [GeneratedRegex("^usr_[A-Za-z0-9_-]{1,96}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserIdRegex();
}
