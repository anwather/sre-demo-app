using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

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
        const int statusCode = StatusCodes.Status500InternalServerError;
        const string title = "Unexpected server error";

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
                Detail = "The server encountered an unexpected error.",
                Extensions = { ["traceId"] = httpContext.TraceIdentifier }
            },
            Exception = exception
        });
    }
}
