using System.Text.Json;
using ApexRacers.Api.Middleware;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApexRacers.Tests.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    private static async Task<(int Status, string ContentType, JsonElement Body)> InvokeWith(
        Exception ex,
        ILogger<ExceptionHandlingMiddleware>? logger = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        RequestDelegate next = _ => throw ex;

        await new ExceptionHandlingMiddleware(
            next,
            logger ?? NullLogger<ExceptionHandlingMiddleware>.Instance)
            .InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var raw = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        return (context.Response.StatusCode, context.Response.ContentType ?? "", JsonSerializer.Deserialize<JsonElement>(raw));
    }

    [Fact]
    public async Task MappedException_WritesProblemDetailsWithStatusTitleAndMessage()
    {
        var (status, contentType, body) = await InvokeWith(new InvalidOperationException("Email already registered."));

        Assert.Equal(400, status);
        Assert.StartsWith("application/problem+json", contentType);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal("Bad Request", body.GetProperty("title").GetString());
        Assert.Equal("Email already registered.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task NotFound_MapsTo404WithMessage()
    {
        var (status, _, body) = await InvokeWith(new KeyNotFoundException("no such flag"));

        Assert.Equal(404, status);
        Assert.Equal("Not Found", body.GetProperty("title").GetString());
        Assert.Equal("no such flag", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Unauthorized_MapsTo401()
    {
        var (status, _, body) = await InvokeWith(new UnauthorizedAccessException("denied"));

        Assert.Equal(401, status);
        Assert.Equal("Unauthorized", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task IRacingNotConfigured_MapsTo503WithFriendlyMessage()
    {
        var (status, _, body) = await InvokeWith(new IRacingNotConfiguredException());

        Assert.Equal(503, status);
        Assert.Equal("Service Unavailable", body.GetProperty("title").GetString());
        Assert.Contains("not configured", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task IRacingNotLinked_WritesExactTyped409Contract()
    {
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var (status, contentType, body) = await InvokeWith(new IRacingNotLinkedException(), logger);

        Assert.Equal(409, status);
        Assert.StartsWith("application/json", contentType);
        Assert.Equal(2, body.EnumerateObject().Count());
        Assert.Equal("IRACING_NOT_LINKED", body.GetProperty("code").GetString());
        Assert.Equal(IRacingNotLinkedException.DefaultMessage, body.GetProperty("message").GetString());
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task UnexpectedException_MapsTo500AndHidesMessage()
    {
        var (status, _, body) = await InvokeWith(new Exception("super secret internal detail"));

        Assert.Equal(500, status);
        Assert.Equal("Internal Server Error", body.GetProperty("title").GetString());
        Assert.Equal("An unexpected error occurred.", body.GetProperty("detail").GetString());
    }

    /// <summary>
    /// Drives the middleware with <c>RequestAborted</c> already signalled, and returns the
    /// raw body so the "nothing was written" case can be asserted (the JSON helper above
    /// cannot parse an empty body).
    /// </summary>
    private static async Task<(int Status, string RawBody, FakeLogger<ExceptionHandlingMiddleware> Logger)>
        InvokeAborted(Exception ex, bool aborted = true, bool responseStarted = false)
    {
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var context = new DefaultHttpContext();
        var body = new MemoryStream();

        if (responseStarted)
            context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature { Body = body });
        else
            context.Response.Body = body;

        if (aborted)
            context.RequestAborted = new CancellationToken(canceled: true);

        RequestDelegate next = _ => throw ex;
        await new ExceptionHandlingMiddleware(next, logger).InvokeAsync(context);

        body.Seek(0, SeekOrigin.Begin);
        var raw = await new StreamReader(body).ReadToEndAsync(TestContext.Current.CancellationToken);
        return (context.Response.StatusCode, raw, logger);
    }

    [Fact]
    public async Task ClientDisconnect_SetsClientClosedRequestAndWritesNoBody()
    {
        // Nobody is left to receive a ProblemDetails payload; writing one would just
        // throw again on a dead connection.
        var (status, raw, _) = await InvokeAborted(new OperationCanceledException("Query was cancelled"));

        Assert.Equal(499, status);
        Assert.Empty(raw);
    }

    [Fact]
    public async Task ClientDisconnect_DoesNotLogAtErrorLevel()
    {
        // The whole point of the change: a browser navigating away is not a server fault
        // and must not page anyone.
        var (_, _, logger) = await InvokeAborted(new OperationCanceledException("The operation was canceled."));

        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug);
    }

    [Fact]
    public async Task AbortedBadHttpRequest_IsTreatedAsDisconnect()
    {
        // Kestrel's "Unexpected end of request content." on an aborted connection.
        var (status, raw, logger) = await InvokeAborted(
            new BadHttpRequestException("Unexpected end of request content."));

        Assert.Equal(499, status);
        Assert.Empty(raw);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task CancellationWithoutClientAbort_StillMapsTo500AndLogsError()
    {
        // Regression guard: a server-side cancellation with the client still waiting is a
        // real failure and must keep its 500 and its Error log.
        var (status, raw, logger) = await InvokeAborted(
            new OperationCanceledException("internal timeout"), aborted: false);

        Assert.Equal(500, status);
        Assert.Contains("An unexpected error occurred.", raw);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ClientDisconnectAfterResponseStarted_IsSwallowedNotRethrown()
    {
        // The pre-existing HasStarted branch rethrows after logging at Error. A disconnect
        // must not take that path: the status is already on the wire, so it is left alone,
        // but the exception is swallowed rather than escalated.
        var (_, _, logger) = await InvokeAborted(
            new OperationCanceledException("aborted mid-write"), responseStarted: true);

        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public string? ReasonPhrase { get; set; }
        public int StatusCode { get; set; } = 200;
        public void OnCompleted(Func<object, Task> callback, object state) { }
        public void OnStarting(Func<object, Task> callback, object state) { }
    }

    [Fact]
    public async Task NoException_PassesThroughUntouched()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        await new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance)
            .InvokeAsync(context);

        Assert.True(called);
        Assert.Equal(200, context.Response.StatusCode);
    }
}
