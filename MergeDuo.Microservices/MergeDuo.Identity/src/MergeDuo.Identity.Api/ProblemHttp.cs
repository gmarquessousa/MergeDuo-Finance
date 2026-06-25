namespace MergeDuo.Identity.Api;

public static class ProblemHttp
{
    public static IResult InvalidRequest(string detail = "Invalid request.") =>
        Problem(StatusCodes.Status400BadRequest, "invalid_request", detail);

    public static IResult InvalidGoogleToken() =>
        Problem(StatusCodes.Status401Unauthorized, "invalid_google_token", "Invalid Google token.");

    public static IResult InvalidRefreshToken() =>
        Problem(StatusCodes.Status401Unauthorized, "invalid_refresh_token", "Invalid refresh token.");

    public static IResult DeviceRevoked() =>
        Problem(StatusCodes.Status401Unauthorized, "device_revoked", "Device is revoked.");

    public static IResult UserDeleted() =>
        Problem(StatusCodes.Status403Forbidden, "user_deleted", "User was deleted.");

    public static IResult HandleTaken() =>
        Problem(StatusCodes.Status409Conflict, "handle_already_taken", "Handle already taken.");

    public static IResult EmailRegistered() =>
        Problem(StatusCodes.Status409Conflict, "email_already_registered", "Email already registered.");

    public static IResult AvatarTooLarge() =>
        Problem(StatusCodes.Status413PayloadTooLarge, "avatar_too_large", "Avatar is too large.");

    public static IResult UnsupportedAvatarType() =>
        Problem(StatusCodes.Status415UnsupportedMediaType, "unsupported_avatar_type", "Unsupported avatar type.");

    public static IResult DependencyUnavailable() =>
        Problem(StatusCodes.Status503ServiceUnavailable, "identity_dependency_unavailable", "Dependency unavailable.");

    public static IResult Problem(int status, string code, string detail) =>
        Results.Problem(
            statusCode: status,
            title: code,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
