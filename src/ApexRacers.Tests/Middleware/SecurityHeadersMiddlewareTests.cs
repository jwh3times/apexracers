using ApexRacers.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ApexRacers.Tests.Middleware;

public class SecurityHeadersMiddlewareTests
{
    private static async Task<DefaultHttpContext> InvokeAsync(bool https = false)
    {
        var context = new DefaultHttpContext();
        context.Request.IsHttps = https;

        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        await new SecurityHeadersMiddleware(next).InvokeAsync(context);

        Assert.True(nextCalled);
        return context;
    }

    [Fact]
    public async Task AddsBaselineSecurityHeaders()
    {
        var context = await InvokeAsync();
        var headers = context.Response.Headers;

        Assert.Equal("nosniff", headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", headers["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"].ToString());
        Assert.Equal("camera=(), geolocation=(), microphone=()", headers["Permissions-Policy"].ToString());
        Assert.Equal("frame-ancestors 'none'", headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    public async Task HttpRequest_OmitsHsts()
    {
        var context = await InvokeAsync(https: false);

        Assert.False(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public async Task HttpsRequest_AddsHsts()
    {
        var context = await InvokeAsync(https: true);

        Assert.Equal(
            "max-age=31536000; includeSubDomains",
            context.Response.Headers["Strict-Transport-Security"].ToString());
    }
}
