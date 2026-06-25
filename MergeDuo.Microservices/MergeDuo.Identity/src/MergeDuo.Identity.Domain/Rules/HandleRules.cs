using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MergeDuo.Identity.Domain.Rules;

public static partial class HandleRules
{
    public static bool IsValid(string? handle) => handle is not null && HandleRegex().IsMatch(handle);

    public static string Normalize(string handle) => handle.Trim().ToLowerInvariant();

    public static IEnumerable<string> Candidates(string? name, string email)
    {
        var localPart = email.Split('@', 2)[0];
        var baseSlug = Slugify(string.IsNullOrWhiteSpace(name) ? localPart : name);
        if (baseSlug.Length < 2)
        {
            baseSlug = Slugify(localPart);
        }

        if (baseSlug.Length < 2)
        {
            baseSlug = "user";
        }

        baseSlug = baseSlug[..Math.Min(baseSlug.Length, 25)];
        yield return "@" + baseSlug;

        for (var i = 2; ; i++)
        {
            var suffix = i.ToString(CultureInfo.InvariantCulture);
            var maxBase = Math.Min(baseSlug.Length, 30 - suffix.Length);
            yield return "@" + baseSlug[..maxBase] + suffix;
        }
    }

    public static string Slugify(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (c is '.' or '_' && builder.Length > 0)
            {
                builder.Append(c);
            }
        }

        return Regex.Replace(builder.ToString(), "[._]{2,}", "_").Trim('.', '_');
    }

    [GeneratedRegex("^@[a-z0-9_.]{2,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex HandleRegex();
}
