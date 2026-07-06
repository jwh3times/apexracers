using System.Diagnostics;

namespace ApexRacers.Api.Middleware;

/// <summary>
/// Emits one structured log entry per request (method, path, status code, elapsed time,
/// client IP) so hosted environments get request-level observability without a new
/// dependency. Health probes (<c>/healthz</c>, <c>/ready</c>) are skipped — the platform
/// polls them every few seconds and they carry no useful signal. Registered outermost
/// (before <see cref="ExceptionHandlingMiddleware"/>) so elapsed time covers the whole
/// pipeline and the logged status code reflects the final response, including any
/// exception → problem+json mapping.
/// </summary>
public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/healthz")
            || context.Request.Path.StartsWithSegments("/ready"))
        {
            await next(context);
            return;
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            // Runs after next(context) whether it completed normally or threw, so the
            // logged StatusCode reflects the final response (including a downstream
            // exception mapped to 500 by ExceptionHandlingMiddleware). The exception
            // itself is not caught here — it propagates after logging.
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            var statusCode = context.Response.StatusCode;
            var level = statusCode switch
            {
                >= 500 => LogLevel.Error,
                >= 400 => LogLevel.Warning,
                _ => LogLevel.Information,
            };

            logger.Log(
                level,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms from {ClientIp}",
                context.Request.Method,
                context.Request.Path,
                statusCode,
                Math.Round(elapsedMs, 1),
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }
    }
}
