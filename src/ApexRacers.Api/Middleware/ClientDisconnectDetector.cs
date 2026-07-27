using Microsoft.AspNetCore.Http;

namespace ApexRacers.Api.Middleware;

/// <summary>
/// Pure predicate for "the client went away before we finished responding", extracted as a
/// standalone function so it can be unit-tested without driving the request pipeline
/// (mirrors <see cref="ExceptionStatusMapper"/>).
/// <para>
/// A disconnect is not a server fault, but it unwinds as an exception and would otherwise
/// be logged at Error and answered with a 500 nobody is left to receive. Browsers cause
/// these routinely by navigating away from a page with requests in flight.
/// </para>
/// <para>
/// Detection deliberately requires <c>requestAborted</c> rather than matching on exception
/// type alone, because both types below have legitimate non-disconnect causes: an
/// <see cref="OperationCanceledException"/> can come from a server-side timeout, and a
/// <see cref="BadHttpRequestException"/> from a genuinely malformed request. The
/// cancellation token is what distinguishes "the connection dropped" from "we failed".
/// Getting it wrong is one-sided: an unsignalled token just falls through to the existing
/// 500 path, so the failure mode is a noisy log, never a swallowed bug.
/// </para>
/// </summary>
public static class ClientDisconnectDetector
{
    /// <summary>
    /// nginx's non-standard "Client Closed Request". It never reaches the client — the
    /// connection is already gone — but it keeps the access log honest about why the
    /// request ended and separates these from real 500s in dashboards and alerts.
    /// </summary>
    public const int StatusClientClosedRequest = 499;

    /// <param name="ex">The exception that unwound out of the pipeline.</param>
    /// <param name="requestAborted">
    /// Whether <c>HttpContext.RequestAborted</c> has been signalled — i.e. the server
    /// observed the connection drop.
    /// </param>
    public static bool IsClientDisconnect(Exception ex, bool requestAborted) =>
        requestAborted && ex is OperationCanceledException or BadHttpRequestException;
}
