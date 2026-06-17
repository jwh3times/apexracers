using System.Text.Json;
using ApexRacers.Api.Middleware;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApexRacers.Tests.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    private static async Task<(int Status, string ContentType, JsonElement Body)> InvokeWith(Exception ex)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        RequestDelegate next = _ => throw ex;

        await new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance)
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
    public async Task UnexpectedException_MapsTo500AndHidesMessage()
    {
        var (status, _, body) = await InvokeWith(new Exception("super secret internal detail"));

        Assert.Equal(500, status);
        Assert.Equal("Internal Server Error", body.GetProperty("title").GetString());
        Assert.Equal("An unexpected error occurred.", body.GetProperty("detail").GetString());
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
