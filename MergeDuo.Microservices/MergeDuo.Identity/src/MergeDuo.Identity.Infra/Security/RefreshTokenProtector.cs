using System.Security.Cryptography;
using System.Text;
using MergeDuo.Identity.Domain.Abstractions;
using MergeDuo.Identity.Domain.Options;

namespace MergeDuo.Identity.Infra.Security;

public sealed class RefreshTokenProtector(RefreshTokenOptions options) : IRefreshTokenProtector
{
    public IssuedRefreshToken Issue(string userId, string deviceId, string sessionId)
    {
        var random = Base64Url(RandomNumberGenerator.GetBytes(32));
        var token = $"v1.{userId}.{deviceId}.{sessionId}.{random}";
        return new IssuedRefreshToken(token, Hash(token));
    }

    public ParsedRefreshToken? Parse(string token)
    {
        var parts = token.Split('.', 5);
        if (parts.Length != 5 || parts[0] != "v1")
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(parts[1])
            || string.IsNullOrWhiteSpace(parts[2])
            || string.IsNullOrWhiteSpace(parts[3])
            || string.IsNullOrWhiteSpace(parts[4])
            ? null
            : new ParsedRefreshToken(parts[1], parts[2], parts[3]);
    }

    public string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token + options.Pepper));
        return "sha256:" + Base64Url(bytes);
    }

    public bool FixedTimeEquals(string token, string expectedHash)
    {
        var actual = Encoding.UTF8.GetBytes(Hash(token));
        var expected = Encoding.UTF8.GetBytes(expectedHash);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
