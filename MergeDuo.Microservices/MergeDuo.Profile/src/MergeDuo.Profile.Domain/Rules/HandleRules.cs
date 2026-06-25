using System.Text.RegularExpressions;

namespace MergeDuo.Profile.Domain.Rules;

public static partial class HandleRules
{
    public static string Normalize(string handle)
    {
        var trimmed = handle.Trim().ToLowerInvariant();
        return trimmed.StartsWith('@') ? trimmed : "@" + trimmed;
    }

    public static bool IsValid(string? handle) =>
        handle is not null && HandleRegex().IsMatch(handle);

    [GeneratedRegex("^@[a-z0-9_.]{2,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex HandleRegex();
}
