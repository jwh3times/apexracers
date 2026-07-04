namespace ApexRacers.Api.Middleware;

/// <summary>
/// Adds baseline security response headers to every response (API + SPA static files).
/// CSP is limited to frame-ancestors — a full CSP needs an inline-style/script audit of
/// the Vite bundle first (tracked in ROADMAP). HSTS is only meaningful over HTTPS; behind
/// the App Service front end, Request.IsHttps reflects the client scheme once
/// ASPNETCORE_FORWARDEDHEADERS_ENABLED is set (see deployTODO.md).
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
        headers["Content-Security-Policy"] = "frame-ancestors 'none'";

        if (context.Request.IsHttps)
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        await next(context);
    }
}
