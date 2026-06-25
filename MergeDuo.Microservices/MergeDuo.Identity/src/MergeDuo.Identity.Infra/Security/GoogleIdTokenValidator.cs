using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MergeDuo.Identity.Domain.Abstractions;
using MergeDuo.Identity.Domain.Options;
using Microsoft.IdentityModel.Tokens;

namespace MergeDuo.Identity.Infra.Security;

public sealed class GoogleIdTokenValidator(
    HttpClient httpClient,
    GoogleOptions options,
    TimeProvider timeProvider) : IGoogleIdTokenValidator
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private JsonWebKeySet? _cachedKeys;
    private DateTimeOffset _expiresAt;

    public async Task<GooglePrincipal> ValidateAsync(
        string idToken,
        string expectedNonce,
        CancellationToken cancellationToken)
    {
        var keys = await GetKeysAsync(forceRefresh: false, cancellationToken);
        try
        {
            return ValidateWithKeys(idToken, expectedNonce, keys);
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            keys = await GetKeysAsync(forceRefresh: true, cancellationToken);
            return ValidateWithKeys(idToken, expectedNonce, keys);
        }
    }

    private GooglePrincipal ValidateWithKeys(string idToken, string expectedNonce, JsonWebKeySet keys)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(idToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],
            ValidateAudience = true,
            ValidAudience = options.ClientId,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys.Keys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        }, out _);

        var nonce = principal.FindFirst("nonce")?.Value;
        if (string.IsNullOrWhiteSpace(nonce) || nonce != expectedNonce)
        {
            throw new SecurityTokenValidationException("Invalid nonce.");
        }

        var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value ?? principal.FindFirst("email")?.Value;
        var emailVerifiedValue = principal.FindFirst("email_verified")?.Value;
        var emailVerified = string.Equals(emailVerifiedValue, "true", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(email) || !emailVerified)
        {
            throw new SecurityTokenValidationException("Google token is missing required claims.");
        }

        var jwt = handler.ReadJwtToken(idToken);
        if (jwt.IssuedAt > timeProvider.GetUtcNow().AddMinutes(2).UtcDateTime)
        {
            throw new SecurityTokenValidationException("Token issued in the future.");
        }

        return new GooglePrincipal(
            Subject: sub,
            Email: email,
            EmailVerified: true,
            Name: principal.FindFirst("name")?.Value,
            PictureUrl: principal.FindFirst("picture")?.Value,
            HostedDomain: principal.FindFirst("hd")?.Value);
    }

    private async Task<JsonWebKeySet> GetKeysAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cachedKeys is not null && _expiresAt > timeProvider.GetUtcNow())
        {
            return _cachedKeys;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cachedKeys is not null && _expiresAt > timeProvider.GetUtcNow())
            {
                return _cachedKeys;
            }

            try
            {
                using var response = await httpClient.GetAsync(options.JwksUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                _cachedKeys = new JsonWebKeySet(json);
                _expiresAt = timeProvider.GetUtcNow().Add(CacheDuration(response));
                return _cachedKeys;
            }
            catch (Exception ex) when (
                !cancellationToken.IsCancellationRequested &&
                ex is HttpRequestException or OperationCanceledException or ArgumentException)
            {
                throw new IdentityDependencyException("Google JWKS dependency unavailable.", ex);
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static TimeSpan CacheDuration(HttpResponseMessage response)
    {
        if (response.Headers.CacheControl?.MaxAge is { } maxAge && maxAge > TimeSpan.Zero)
        {
            return maxAge;
        }

        return TimeSpan.FromHours(1);
    }
}
