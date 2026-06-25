using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MergeDuo.Partnership.Domain.Exceptions;

namespace MergeDuo.Partnership.Domain.Rules;

public static partial class InviteTokenRules
{
    public const int MinimumEntropyBytes = 16;
    public const int MaximumTokenLength = 160;

    public static string Generate(int entropyBytes)
    {
        if (entropyBytes < MinimumEntropyBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(entropyBytes), "Invite tokens require at least 128 bits of entropy.");
        }

        var bytes = RandomNumberGenerator.GetBytes(entropyBytes);
        return Base64UrlEncode(bytes);
    }

    public static void EnsureValid(string? token)
    {
        if (!IsValid(token))
        {
            throw new PartnershipBadRequestException("invalid_invite_token", "Invalid invite token.");
        }
    }

    public static bool IsValid(string? token) =>
        !string.IsNullOrWhiteSpace(token)
        && token.Length <= MaximumTokenLength
        && TokenRegex().IsMatch(token);

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    [GeneratedRegex("^[A-Za-z0-9_-]{22,160}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
