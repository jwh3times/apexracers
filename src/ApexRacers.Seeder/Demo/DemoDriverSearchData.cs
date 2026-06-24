using ApexRacers.Api.Dtos;
using ApexRacers.Core;

namespace ApexRacers.Seeder.Demo;

/// <summary>Curated synthetic driver-search results keyed by lowercased term (matching RivalService's
/// Trim().ToLowerInvariant() normalization + ≥2-char rule). Arbitrary unseeded terms still 503 — a
/// documented demo caveat (infinite terms can't be pre-seeded).</summary>
public static class DemoDriverSearchData
{
    private static readonly DriverSearchResultDto Demo = new(DemoData.DriverCustId, "Demo Driver");
    private static readonly DriverSearchResultDto Rival = new(DemoData.RivalCustId, "Rival Racer");

    public static readonly IReadOnlyDictionary<string, List<DriverSearchResultDto>> Terms =
        new Dictionary<string, List<DriverSearchResultDto>>
        {
            ["demo"] = [Demo],
            ["rival"] = [Rival],
            ["riv"] = [Rival],
            ["racer"] = [Rival],
            ["rac"] = [Rival],
            ["driver"] = [Demo, Rival, new(100_003, "Driver 100003"), new(100_004, "Driver 100004")],
            ["dri"] = [Demo, new(100_003, "Driver 100003")],
        };
}
