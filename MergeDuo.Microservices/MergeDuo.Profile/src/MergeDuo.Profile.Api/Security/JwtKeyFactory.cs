using System.Security.Cryptography;
using MergeDuo.Profile.Domain.Options;
using Microsoft.IdentityModel.Tokens;

namespace MergeDuo.Profile.Api.Security;

public static class JwtKeyFactory
{
    public static SecurityKey? TryCreateStaticKey(JwtOptions options)
    {
        var pem = !IsPlaceholder(options.PublicKeyPem)
            ? options.PublicKeyPem
            : !IsPlaceholder(options.PrivateKeyPem)
                ? options.PrivateKeyPem
                : null;

        if (string.IsNullOrWhiteSpace(pem))
        {
            return null;
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new RsaSecurityKey(rsa) { KeyId = options.KeyId };
    }

    private static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("<", StringComparison.Ordinal);
}
