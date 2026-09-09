using ApexRacers.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ApexRacers.Tests.Middleware;

public class SecurityHeadersMiddlewareTests
{
    private static async Task<DefaultHttpContext> InvokeAsync(bool https = false, string path = "/")
    {
        var context = new DefaultHttpContext();
        context.Request.IsHttps = https;
        context.Request.Path = path;

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
        Assert.Equal(ContentSecurityPolicy.Spa, headers["Content-Security-Policy"].ToString());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/assets/app.js")]
    [InlineData("/api/auth/login")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/v1")]
    [InlineData("/scalar/not-a-real/route")]
    public async Task EveryPathGetsStrictSpaPolicy(string path)
    {
        var context = await InvokeAsync(path: path);
        var policy = context.Response.Headers.ContentSecurityPolicy.ToString();

        Assert.Equal("default-src 'self'; script-src 'self'; style-src 'self'; " +
            "img-src 'self' data: https://images-static.iracing.com; font-src 'self'; connect-src 'self'; " +
            "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'", policy);
        Assert.DoesNotContain("unsafe-", policy);
        Assert.DoesNotContain("nonce-", policy);
    }

    [Fact]
    public void ScalarPolicyAllowsInlineStylesButRequiresNonceForInlineScripts()
    {
        var policy = ContentSecurityPolicy.ForScalar("test-nonce");

        Assert.Equal("default-src 'self'; script-src 'self' 'nonce-test-nonce'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; " +
            "base-uri 'self'; form-action 'self'; frame-ancestors 'none'", policy);
        Assert.NotEqual(policy, ContentSecurityPolicy.ForScalar("another-nonce"));
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
