using ApexRacers.Core;

namespace ApexRacers.Api.Services;

/// <summary>Selects provenance from current server-side flags and role eligibility.</summary>
public sealed class IRacingRequestContext(FeatureFlagEligibility flags, IRacingDataScope scope)
{
    public async Task<DataProvenance> SelectAsync(Guid? userId, CancellationToken ct)
    {
        var active = userId is { } id
            ? await flags.GetActiveForUserAsync(id, ct)
            : await flags.GetActiveForRoleAsync("Standard", ct);
        var provenance = active.Any(f => f.Key == "iracing-demo") ? DataProvenance.Demo
            : active.Any(f => f.Key == "iracing-live") ? DataProvenance.Real
            : DataProvenance.Unknown;
        scope.Select(provenance);
        return provenance;
    }
}
