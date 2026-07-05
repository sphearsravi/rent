using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Sentry;

namespace RentCollection.Web.Middleware;

/// <summary>
/// Catches every unhandled exception. API requests get a JSON ProblemDetails;
/// MVC requests fall through to the configured error page. Either way we log
/// the exception with the RequestId so it can be correlated, and forward it
/// to Sentry (no-op when the DSN isn't configured).
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var requestId = httpContext.TraceIdentifier;
        _logger.LogError(exception, "Unhandled exception at {Path}. RequestId={RequestId}",
            httpContext.Request.Path, requestId);

        // Sentry — capture the exception with the request id tagged so the
        // event can be correlated with the Serilog trail. SentrySdk is a
        // no-op when no DSN is configured, so this is safe in dev.
        SentrySdk.CaptureException(exception, scope =>
        {
            scope.SetTag("requestId", requestId);
            scope.SetTag("path", httpContext.Request.Path);
        });

        // For API requests respond with JSON ProblemDetails; let MVC pages render
        // their own error view via the default exception page middleware.
        if (!httpContext.Request.Path.StartsWithSegments("/api"))
            return false;

        if (httpContext.Response.HasStarted)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            Title = "An unexpected error occurred.",
            Status = StatusCodes.Status500InternalServerError,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["requestId"] = requestId;

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
