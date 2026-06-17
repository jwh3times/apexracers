using ApexRacers.Api.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

public static class ControllerExtensions
{
    public const string IRacingNotLinkedCode = "IRACING_NOT_LINKED";

    /// <summary>
    /// 409 Conflict carrying a typed code so the client can render a "link your
    /// iRacing account" prompt instead of a generic error or an empty result.
    /// </summary>
    public static ConflictObjectResult IRacingNotLinked(this ControllerBase controller) =>
        controller.Conflict(new NotLinkedDto(
            IRacingNotLinkedCode,
            "Link your iRacing customer ID in Settings to view this data."));
}
