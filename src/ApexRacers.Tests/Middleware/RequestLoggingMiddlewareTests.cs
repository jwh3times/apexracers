using ApexRacers.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ApexRacers.Tests.Middleware;

/// <summary>
/// Captures every <see cref="ILogger{TCategoryName}"/> call so tests can assert on the
/// level and formatted message without a mocking framework's logger-matching quirks.
/// </summary>
public class FakeLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}

public class RequestLoggingMiddlewareTests
{
    private static (FakeLogger<RequestLoggingMiddleware> Logger, DefaultHttpContext Context) InvokeWith(
        RequestDelegate next,
        string path = "/api/foo",
        string method = "GET")
    {
        var logger = new FakeLogger<RequestLoggingMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        var middleware = new RequestLoggingMiddleware(next, logger);
        middleware.InvokeAsync(context).GetAwaiter().GetResult();

        return (logger, context);
    }

    [Fact]
    public void NormalRequest_LogsOneInformationEntryWithMethodPathAndStatus()
    {
        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        var (logger, _) = InvokeWith(next);

        Assert.True(nextCalled);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("GET", entry.Message);
        Assert.Contains("/api/foo", entry.Message);
        Assert.Contains("200", entry.Message);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/ready")]
    public void HealthProbePaths_AreNotLogged(string path)
    {
        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        var (logger, _) = InvokeWith(next, path);

        Assert.True(nextCalled);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void ServerErrorResponse_LogsAtErrorLevel()
    {
        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 500;
            return Task.CompletedTask;
        };

        var (logger, _) = InvokeWith(next);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("500", entry.Message);
    }

    [Fact]
    public void NotFoundResponse_LogsAtWarningLevel()
    {
        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        };

        var (logger, _) = InvokeWith(next);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("404", entry.Message);
    }

    [Fact]
    public void CrlfInRequest_IsStrippedFromLogEntry()
    {
        // A crafted method/path carrying CR/LF must not forge extra log lines (CWE-117).
        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        var (logger, _) = InvokeWith(next, method: "GET\r\nInjected-Line: evil");

        var entry = Assert.Single(logger.Entries);
        Assert.DoesNotContain('\r', entry.Message);
        Assert.DoesNotContain('\n', entry.Message);
        // Only the newlines are removed; the surrounding content is preserved.
        Assert.Contains("GETInjected-Line: evil", entry.Message);
    }

    [Fact]
    public async Task DownstreamThrows_StillLogsAndExceptionPropagates()
    {
        var logger = new FakeLogger<RequestLoggingMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/boom";

        RequestDelegate next = _ => throw new InvalidOperationException("downstream failure");
        var middleware = new RequestLoggingMiddleware(next, logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("GET", entry.Message);
        Assert.Contains("/api/boom", entry.Message);
    }
}
