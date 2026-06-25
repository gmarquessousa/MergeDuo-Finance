namespace MergeDuo.Aggregates.Api;

public static class ProblemHttp
{
    public static IResult Unauthorized() =>
        Problem(StatusCodes.Status401Unauthorized, "unauthorized", "JWT is missing, invalid or expired.");

    public static IResult InvalidUserId() =>
        Problem(StatusCodes.Status400BadRequest, "invalid_user_id", "Invalid user id.");

    public static IResult DependencyUnavailable() =>
        Problem(StatusCodes.Status503ServiceUnavailable, "aggregates_dependency_unavailable", "Aggregates dependency unavailable.");

    public static IResult Problem(int status, string code, string detail) =>
        Results.Problem(
            statusCode: status,
            title: code,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static async Task WriteAsync(HttpContext context, int status, string code, string detail, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(
            new
            {
                type = "about:blank",
                title = code,
                status,
                detail,
                code
            },
            cancellationToken);
    }
}
