using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Http;

namespace ApexRacers.Api.Middleware;

/// <summary>
/// Pure exception → HTTP status mapping for <see cref="ExceptionHandlingMiddleware"/>.
/// Extracted as a standalone function so it can be unit-tested directly without
/// driving the request pipeline (mirrors <c>SubsessionIndexer</c>).
/// </summary>
public static class ExceptionStatusMapper
{
    public static int MapStatusCode(Exception ex) => ex switch
    {
        IRacingNotLinkedException => StatusCodes.Status409Conflict,
        IRacingNotConfiguredException => StatusCodes.Status503ServiceUnavailable,
        ArgumentException => StatusCodes.Status400BadRequest,
        InvalidOperationException => StatusCodes.Status400BadRequest,
        KeyNotFoundException => StatusCodes.Status404NotFound,
        UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status500InternalServerError,
    };
}
