using Microsoft.AspNetCore.Mvc;

namespace MergeDuo.Profile.Api;

public static class ProblemHttp
{
    public static IResult InvalidUserId() =>
        Problem(StatusCodes.Status400BadRequest, "invalid_user_id", "Invalid user id.");

    public static IResult InvalidHandle() =>
        Problem(StatusCodes.Status400BadRequest, "invalid_handle", "Invalid handle.");

    public static IResult Unauthorized() =>
        Problem(StatusCodes.Status401Unauthorized, "unauthorized", "JWT is missing, invalid or expired.");

    public static IResult UserDeleted() =>
        Problem(StatusCodes.Status403Forbidden, "user_deleted", "User was deleted.");

    public static IResult ProfileNotFound() =>
        Problem(StatusCodes.Status404NotFound, "profile_not_found", "Profile not found.");

    public static IResult DependencyUnavailable() =>
        Problem(StatusCodes.Status503ServiceUnavailable, "profile_dependency_unavailable", "Profile dependency unavailable.");

    public static IResult Problem(int status, string code, string detail) =>
        Results.Problem(
            statusCode: status,
            title: code,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static async Task WriteAsync(
        HttpContext context,
        int status,
        string code,
        string detail,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = detail
        };
        problem.Extensions["code"] = code;

        await context.Response.WriteAsJsonAsync(problem, cancellationToken);
    }
}
