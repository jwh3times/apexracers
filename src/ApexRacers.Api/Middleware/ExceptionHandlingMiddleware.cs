using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into RFC-7807 <c>application/problem+json</c>
/// responses, with the status code chosen by <see cref="ExceptionStatusMapper"/>.
/// Registered first in the pipeline so it wraps everything downstream. Controllers
/// may still return explicit status results (e.g. 423 lockout, 404, 501) — those are
/// ordinary results, not exceptions, and bypass this layer untouched.
/// </summary>
public class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            // Checked before the HasStarted branch below: a disconnect must never take the
            // rethrow-at-Error path either. There is no client left to answer, so this is
            // recorded and swallowed rather than escalated. Kept at Debug because
            // RequestLoggingMiddleware already emits the per-request line (at 499, and at
            // Information) — this only carries the exception detail for anyone who wants it.
            if (ClientDisconnectDetector.IsClientDisconnect(ex, context.RequestAborted.IsCancellationRequested))
            {
                logger.LogDebug(ex, "Client disconnected before the response completed.");

                if (!context.Response.HasStarted)
                    context.Response.StatusCode = ClientDisconnectDetector.StatusClientClosedRequest;

                return;
            }

            if (context.Response.HasStarted)
            {
                logger.LogError(ex, "Exception thrown after the response started; cannot write ProblemDetails.");
                throw;
            }

            var statusCode = ExceptionStatusMapper.MapStatusCode(ex);
            if (statusCode >= StatusCodes.Status500InternalServerError)
                logger.LogError(ex, "Unhandled exception");
            else
                logger.LogWarning(ex, "Request failed with status {StatusCode}", statusCode);

            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = TitleFor(statusCode),
                // Mapped exceptions carry intentional, safe messages; only the catch-all
                // 500 hides its message so internal details never leak to the client.
                Detail = statusCode == StatusCodes.Status500InternalServerError
                    ? "An unexpected error occurred."
                    : ex.Message,
            };

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(problem, JsonOptions), context.RequestAborted);
        }
    }

    private static string TitleFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
        _ => "Internal Server Error",
    };
}
