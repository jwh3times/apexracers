using ApexRacers.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ApexRacers.Api.Services;

/// <summary>
/// Keeps feature routes without protected Driver dispatch unavailable. Runs before
/// model binding so an attributed upload cannot start its legacy persistence path.
/// </summary>
public sealed class LegacyDriverAccessGuard(IRacingDataScope scope) : IAsyncResourceFilter
{
    private static readonly HashSet<string> IndependentControllers = new(StringComparer.Ordinal)
    {
        "ScopedDrivers", "DriverPrivacy", "Auth", "Admin", "FeatureFlags", "Cars", "Tracks", "Series", "Schedule",
    };

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var controller = (context.ActionDescriptor as ControllerActionDescriptor)?.ControllerName;
        var independent = controller is not null && IndependentControllers.Contains(controller);
        if (!independent) context.HttpContext.Response.Headers.CacheControl = "no-store";
        // The old upload workflow establishes attribution from claims and recorder IDs. Demo
        // cannot turn its real uploads into synthetic evidence or grant real personal access.
        if (!independent && (controller == "Telemetry" || scope.Provenance != DataProvenance.Demo))
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "This Driver workflow is unavailable while authorization is being implemented.",
            })
            { StatusCode = StatusCodes.Status503ServiceUnavailable };
            return;
        }
        await next();
    }
}
