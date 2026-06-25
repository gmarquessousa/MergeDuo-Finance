using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using MergeDuo.Identity.Domain.Options;
using MergeDuo.Identity.Infra.Security;
using MergeDuo.Identity.Tests.Fakes;
using Microsoft.IdentityModel.Tokens;

namespace MergeDuo.Identity.Tests;

public sealed class GoogleIdTokenValidatorTests
{
    [Fact]
    public async Task Validates_google_claims_with_fake_jwks()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "google-key" };
        var publicKey = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "google-key" };
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(publicKey);
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        var jwks = JsonSerializer.Serialize(new { keys = new[] { jwk } });
        using var httpClient = new HttpClient(new StaticHttpMessageHandler(jwks));
        var validator = new GoogleIdTokenValidator(
            httpClient,
            new GoogleOptions { ClientId = "google-client", JwksUrl = "https://google.test/jwks" },
            TimeProvider.System);

        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "https://accounts.google.com",
            audience: "google-client",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, "google-sub"),
                new Claim(JwtRegisteredClaimNames.Email, "user@example.com"),
                new Claim("email_verified", "true"),
                new Claim("nonce", "nonce-1"),
                new Claim("name", "Merge Duo")
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256)));

        var principal = await validator.ValidateAsync(token, "nonce-1", CancellationToken.None);

        Assert.Equal("google-sub", principal.Subject);
        Assert.Equal("user@example.com", principal.Email);
        Assert.Equal("Merge Duo", principal.Name);
    }
}
