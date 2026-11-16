namespace ApexRacers.Api.Middleware;

/// <summary>
/// The liveness (<c>/healthz</c>) and readiness (<c>/ready</c>) endpoints the platform polls. They
/// carry no signal worth keeping, so neither the request log nor request telemetry records them.
/// </summary>
public static class HealthProbePaths
{
    public static bool Matches(PathString path) =>
        path.StartsWithSegments("/healthz") || path.StartsWithSegments("/ready");
}
