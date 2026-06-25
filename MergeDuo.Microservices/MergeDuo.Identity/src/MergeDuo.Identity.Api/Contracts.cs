using System.Text.Json.Serialization;
using MergeDuo.Identity.Domain.Documents;

namespace MergeDuo.Identity.Api;

public sealed record GoogleChallengeResponse(string Nonce, string CsrfToken, int ExpiresIn, string ChallengeToken);

public sealed record GoogleCallbackRequest(
    string IdToken,
    bool RememberMe,
    DeviceRequest Device,
    string? ChallengeToken = null);

public sealed record GoogleRedirectStartRequest(
    bool RememberMe,
    string? ReturnPath,
    DeviceRequest Device);

public sealed record GoogleRedirectStartResponse(
    string Nonce,
    string State,
    string LoginUri,
    int ExpiresIn);

public sealed record DeviceRequest(
    string InstallId,
    string Platform,
    string? UserAgent,
    string? Model,
    string? OsVersion,
    string? AppVersion);

public sealed record AuthTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string CsrfToken,
    UserMeResponse User,
    string DeviceId);

public sealed record UserSummaryResponse(
    string Id,
    string Name,
    string Handle,
    string Email,
    string? AvatarUrl);

public sealed record UserMeResponse(
    string Id,
    string Name,
    string Handle,
    string Email,
    string? Phone,
    string AvatarInitials,
    string? AvatarUrl,
    string MemberSince,
    string RegisteredAt,
    UserFinancial Financial,
    UserPreferences Preferences,
    UserStats Stats,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

public sealed record PatchUserMeRequest(
    string? Name,
    string? Handle,
    string? Phone,
    UserPreferences? Preferences);

public sealed record AvatarResponse(string AvatarUrl);

public sealed record OpenIdConfigurationResponse(
    string Issuer,
    [property: JsonPropertyName("jwks_uri")] string JwksUri,
    [property: JsonPropertyName("id_token_signing_alg_values_supported")] string[] IdTokenSigningAlgValuesSupported);

public static class ResponseMapping
{
    public static UserSummaryResponse ToSummary(this UserDocument user) =>
        new(user.Id, user.Name, user.Handle, user.Email, user.AvatarUrl ?? user.Auth.Google.PictureUrl);

    public static UserMeResponse ToMe(this UserDocument user) =>
        new(
            user.Id,
            user.Name,
            user.Handle,
            user.Email,
            user.Phone,
            user.AvatarInitials,
            user.AvatarUrl ?? user.Auth.Google.PictureUrl,
            user.MemberSince,
            user.RegisteredAt,
            user.Financial,
            user.Preferences,
            user.Stats,
            user.CreatedAt,
            user.UpdatedAt,
            user.DeletedAt);
}
