using System.Net;
using MergeDuo.Partnership.Domain.Abstractions;
using MergeDuo.Partnership.Domain.Exceptions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace MergeDuo.Partnership.Infra.Cosmos;

internal static class CosmosFailureHandler
{
    private const HttpStatusCode TooManyRequests = (HttpStatusCode)429;

    public static Exception Classify(
        ILogger logger,
        ICosmosDiagnosticsRecorder diagnostics,
        string container,
        string operation,
        string detail,
        string badRequestCode,
        CosmosException ex)
    {
        var throttled = ex.StatusCode == TooManyRequests;
        diagnostics.RecordCosmosOperation(container, operation, ex.RequestCharge, throttled);

        logger.LogError(
            ex,
            "Cosmos failure on {Container}/{Operation}: status={StatusCode} subStatus={SubStatusCode} activityId={ActivityId} requestCharge={RequestCharge}",
            container,
            operation,
            (int)ex.StatusCode,
            ex.SubStatusCode,
            ex.ActivityId,
            ex.RequestCharge);

        return ex.StatusCode switch
        {
            TooManyRequests => new PartnershipThrottledException(
                "partnership_throttled",
                detail,
                ex.RetryAfter,
                ex),
            HttpStatusCode.BadRequest => new PartnershipBadRequestException(
                badRequestCode,
                detail),
            _ => new PartnershipDependencyException(
                "partnership_dependency_unavailable",
                detail,
                ex)
        };
    }
}
