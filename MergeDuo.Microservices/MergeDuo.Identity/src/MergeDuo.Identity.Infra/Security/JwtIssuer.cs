using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MergeDuo.Identity.Domain.Abstractions;
using MergeDuo.Identity.Domain.Options;
using Microsoft.IdentityModel.Tokens;

namespace MergeDuo.Identity.Infra.Security;

public sealed class JwtIssuer(JwtOptions options, TimeProvider timeProvider) : IJwtIssuer
{
    private readonly RsaSecurityKey _key = JwtKeyFactory.CreatePrivateKey(options.PrivateKeyPem, options.KeyId);

    public AccessTokenResult Issue(string userId, string deviceId, string handle)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(options.AccessTokenMinutes);
        var credentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim("userId", userId),
            new Claim("deviceId", deviceId),
            new Claim("handle", handle),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new AccessTokenResult(
            new JwtSecurityTokenHandler().WriteToken(token),
            checked((int)Math.Round((expires - now).TotalSeconds)),
            expires);
    }

    public JsonWebKeySetDto GetJwks()
    {
        var parameters = _key.Rsa!.ExportParameters(false);
        return new JsonWebKeySetDto(
        [
            new JsonWebKeyDto(
                Kty: "RSA",
                Use: "sig",
                Kid: options.KeyId,
                Alg: SecurityAlgorithms.RsaSha256,
                N: Base64Url(parameters.Modulus!),
                E: Base64Url(parameters.Exponent!))
        ]);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class JwtKeyFactory
{
    public static RsaSecurityKey CreatePrivateKey(string pem, string keyId)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new RsaSecurityKey(rsa) { KeyId = keyId };
    }

    public static RsaSecurityKey CreatePublicKey(string pem, string keyId)
    {
        using var privateRsa = RSA.Create();
        privateRsa.ImportFromPem(pem);
        var parameters = privateRsa.ExportParameters(false);
        var publicRsa = RSA.Create(parameters);
        return new RsaSecurityKey(publicRsa) { KeyId = keyId };
    }
}
