namespace ApexRacers.Core;

/// <summary>
/// iRacing reports temperatures and wind speeds in whichever unit the session was configured
/// with, flagged by a companion <c>*_units</c> field. This module owns that domain fact and the
/// conversion constants.
///
/// It exists because the same two converters were written twice — once in the subsession read
/// path and once in the schedule read path — each carrying the identical doc-comment
/// ("0 = Fahrenheit, otherwise Celsius. Pure + testable") and each with its own tests. Both sets
/// passed, because each was written against the copy it sat beside. That is
/// extract-for-testability without locality: the arithmetic was tested, but the load-bearing
/// knowledge — the unit encoding and the constants — was stored twice.
/// </summary>
public static class IRacingUnits
{
    /// <summary>Fahrenheit when <paramref name="units"/> is 0, otherwise already Celsius.</summary>
    public static double ToCelsius(double value, int units) =>
        units == 0 ? Math.Round((value - 32) * 5.0 / 9.0, 1) : Math.Round(value, 1);

    /// <summary>mph when <paramref name="units"/> is 0, otherwise m/s. Returns km/h.</summary>
    public static double ToKph(double value, int units) =>
        units == 0 ? Math.Round(value * 1.60934, 1) : Math.Round(value * 3.6, 1);
}
