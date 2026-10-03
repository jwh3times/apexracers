using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using ApexRacers.Core;

namespace ApexRacers.Api.Middleware;

public sealed class IRacingDataScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IRacingRequestContext request)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            Guid? userId = context.User.Identity?.IsAuthenticated == true
                && Guid.TryParse(context.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
                    ? id : null;
            var provenance = await request.SelectAsync(userId, context.RequestAborted);
            // Publication metadata describes the selected Driver-evidence namespace; account
            // fields and Uploaded Laps retain their own meaning. Caller headers cannot select it.
            context.Response.Headers["X-ApexRacers-Driver-Evidence-Namespace"] = provenance switch
            {
                DataProvenance.Demo => "demo",
                DataProvenance.Real => "real",
                _ => "unavailable",
            };
        }
        await next(context);
    }
}
