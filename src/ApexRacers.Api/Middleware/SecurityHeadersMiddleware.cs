namespace ApexRacers.Api.Middleware;

/// <summary>
/// Adds baseline security response headers to every response (API + SPA static files).
/// The built SPA loads scripts, styles, fonts, and API requests from its own origin.
/// HSTS is only meaningful over HTTPS; behind
/// the App Service front end, Request.IsHttps reflects the client scheme once
/// the forwarded-headers middleware rewrote the scheme (see ForwardedHeadersPolicy).
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=()";
        headers["Content-Security-Policy"] = ContentSecurityPolicy.Spa;

        if (context.Request.IsHttps)
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        await next(context);
    }
}
