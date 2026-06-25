using Microsoft.AspNetCore.Mvc;

namespace MergeDuo.Cards.Api;

public static class ProblemHttp
{
    public static IResult Unauthorized() =>
        Problem(StatusCodes.Status401Unauthorized, "unauthorized", "JWT is missing, invalid or expired.");

    public static IResult InvalidRequest(string detail = "Invalid request.") =>
        Problem(StatusCodes.Status400BadRequest, "invalid_request", detail);

    public static IResult DependencyUnavailable() =>
        Problem(StatusCodes.Status503ServiceUnavailable, "cards_dependency_unavailable", "Cards dependency unavailable.");

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
