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
                // Client-initiated, not a server fault — a browser navigating away from a
                // page with requests in flight produces these routinely. Without this case
                // it would land in the >= 400 Warning bucket and read as investigable.
                ClientDisconnectDetector.StatusClientClosedRequest => LogLevel.Information,
                >= 500 => LogLevel.Error,
                >= 400 => LogLevel.Warning,
                _ => LogLevel.Information,
            };

            logger.Log(
                level,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms from {ClientIp}",
                StripNewlines(context.Request.Method),
                StripNewlines(context.Request.Path.ToString()),
                statusCode,
                Math.Round(elapsedMs, 1),
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }
    }

    // Strip CR/LF from request-derived strings before logging: a percent-encoded
    // %0d%0a in the URL is decoded into Request.Path, so an unsanitized value could
    // forge extra lines in the text log sink (CWE-117 / cs/log-forging). App Insights
    // stores these as structured dimensions; the console/Log Analytics sink renders text.
    private static string StripNewlines(string value) =>
        value.Replace("\r", string.Empty).Replace("\n", string.Empty);
}
