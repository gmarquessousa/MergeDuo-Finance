using System.Security.Claims;

namespace MergeDuo.Identity.Domain.Abstractions;

public interface IGoogleIdTokenValidator
{
    Task<GooglePrincipal> ValidateAsync(string idToken, string expectedNonce, CancellationToken cancellationToken);
}

public sealed record GooglePrincipal(
    string Subject,
    string Email,
    bool EmailVerified,
    string? Name,
    string? PictureUrl,
    string? HostedDomain);

public interface IJwtIssuer
{
    AccessTokenResult Issue(string userId, string deviceId, string handle);
    JsonWebKeySetDto GetJwks();
}

public sealed record AccessTokenResult(string AccessToken, int ExpiresIn, DateTimeOffset ExpiresAt);

public sealed record JsonWebKeySetDto(JsonWebKeyDto[] Keys);

public sealed record JsonWebKeyDto(
    string Kty,
    string Use,
    string Kid,
    string Alg,
    string N,
    string E);

public interface IAvatarStorage
{
    Task<AvatarUploadResult> UploadAsync(
        string userId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken);

    Task DeleteByUrlAsync(string? avatarUrl, CancellationToken cancellationToken);
}

public sealed record AvatarUploadResult(string Url, string BlobName, string ContentHash);

public interface IRefreshTokenProtector
{
    IssuedRefreshToken Issue(string userId, string deviceId, string sessionId);
    ParsedRefreshToken? Parse(string token);
    string Hash(string token);
    bool FixedTimeEquals(string token, string expectedHash);
}

public sealed record IssuedRefreshToken(string Token, string Hash);

public sealed record ParsedRefreshToken(string UserId, string DeviceId, string SessionId);

public sealed record AuthenticatedIdentity(string UserId, string DeviceId, string Handle);

public static class ClaimsPrincipalExtensions
{
    public static AuthenticatedIdentity? ToAuthenticatedIdentity(this ClaimsPrincipal principal)
    {
        var userId = principal.FindFirst("userId")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var deviceId = principal.FindFirst("deviceId")?.Value;
        var handle = principal.FindFirst("handle")?.Value;
        return string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(handle)
            ? null
            : new AuthenticatedIdentity(userId, deviceId, handle);
    }
}
