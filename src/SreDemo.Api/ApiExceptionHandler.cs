using Azure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SreDemo.Api.Faults;

namespace SreDemo.Api;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            StorageFailureInjectedException =>
                (StatusCodes.Status503ServiceUnavailable, "Injected storage dependency failure"),
            RequestFailedException =>
                (StatusCodes.Status503ServiceUnavailable, "Blob Storage dependency unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error")
        };

        logger.LogError(
            exception,
            "Request failed with status code {StatusCode} and trace identifier {TraceIdentifier}",
            statusCode,
            httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = statusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = statusCode == StatusCodes.Status500InternalServerError
                    ? "The server encountered an unexpected error."
                    : exception.Message,
                Extensions = { ["traceId"] = httpContext.TraceIdentifier }
            },
            Exception = exception
        });
    }
}
