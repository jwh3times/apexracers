using System.Globalization;

namespace ApexRacers.Api.Services;

/// <summary>
/// Pure strategy heuristics for a race week. Each function turns one piece of week context
/// (forecast, BoP fuel/tire caps, week-over-week BoP movement) into a flag + human-readable
/// note. Extracted as a pure static helper so every threshold/branch is unit-tested directly
/// (mirrors <see cref="LapAnalysis"/> / <see cref="SharedRaceAnalysis"/>).
/// </summary>
public static class StrategyAnalysis
{
    // Forecast precipitation-chance thresholds (percent) separating Low/Medium/High wet risk.
    private const double MediumRiskPrecipPct = 15.0;
    private const double HighRiskPrecipPct = 40.0;

    /// <summary>
    /// Wet-weather risk from the forecast precipitation chance and whether the week's cars can
    /// run rain tires. Low (&lt;15%), Medium (15–40%), High (≥40%); the advice changes depending
    /// on whether the field is rain-capable.
    /// </summary>
    public static (string Level, string Note) WeatherRisk(double precipChancePct, bool fieldRainCapable)
    {
        if (precipChancePct >= HighRiskPrecipPct)
            return ("High", fieldRainCapable
                ? "Wet running likely — prioritize a rain setup and tire management."
                : "Wet running likely, but the cars aren't rain-capable — expect grip and timing disruption.");

        if (precipChancePct >= MediumRiskPrecipPct)
            return ("Medium", fieldRainCapable
                ? "Showers possible — keep a wet setup ready."
                : "Showers possible — sessions may run dry or be affected.");

        return ("Low", "Dry conditions expected.");
    }

    /// <summary>
    /// Fuel-fill note from the BoP cap. A cap in (0, 100) shortens stints and tends to add a
    /// stop; 0 or ≥100 means no restriction.
    /// </summary>
    public static (bool Capped, string Note) FuelNote(double maxPctFuelFill)
    {
        if (maxPctFuelFill > 0 && maxPctFuelFill < 100)
            return (true, $"Fuel capped at {Trim(maxPctFuelFill)}% — shorter stints; plan an extra stop.");

        return (false, "No fuel-fill restriction.");
    }

    /// <summary>
    /// Tire note from the BoP dry-set cap. A positive cap limits sets (manage wear); 0 means
    /// unlimited.
    /// </summary>
    public static (bool Limited, string Note) TireNote(int maxDryTireSets)
    {
        if (maxDryTireSets > 0)
            return (true, $"Limited to {maxDryTireSets} dry set{(maxDryTireSets == 1 ? "" : "s")} — manage tire wear.");

        return (false, "Unlimited dry tire sets.");
    }

    /// <summary>
    /// Week-over-week BoP movement for a car. Returns the weight (kg) and power (%) deltas vs the
    /// previous week plus a trend label. A nerf is heavier and/or down on power; a buff is lighter
    /// and/or up on power; opposing moves are "Mixed". When there is no previous week the trend is
    /// "—" and deltas are 0.
    /// </summary>
    public static (double WeightDeltaKg, double PowerDeltaPct, string Trend) BopShift(
        double? prevWeightKg, double weightKg, double? prevPowerPct, double powerPct)
    {
        if (prevWeightKg is null || prevPowerPct is null)
            return (0, 0, "—");

        var weightDelta = Math.Round(weightKg - prevWeightKg.Value, 1);
        var powerDelta = Math.Round(powerPct - prevPowerPct.Value, 1);

        if (weightDelta == 0 && powerDelta == 0)
            return (weightDelta, powerDelta, "Unchanged");

        var nerf = weightDelta > 0 || powerDelta < 0;
        var buff = weightDelta < 0 || powerDelta > 0;

        var trend = nerf && buff ? "Mixed"
            : nerf ? "Nerfed"
            : "Buffed";

        return (weightDelta, powerDelta, trend);
    }

    // Renders a fuel-percent without a trailing ".0" (e.g. 92.5 → "92.5", 90 → "90").
    private static string Trim(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}
