namespace ApexRacers.Core.Models;

/// <summary>
/// Balance-of-Performance restrictions for one car in one race week of a season
/// (from the season schedule's <c>car_restrictions</c>). Composite key
/// (SeasonId, WeekNumber, CarId). CarId is stored plain (no FK) so ingestion order
/// never blocks a BoP row; names are resolved from the catalog at read time.
/// </summary>
public class SeasonCarBop
{
    public int SeasonId { get; set; }
    public int WeekNumber { get; set; }
    public int CarId { get; set; }
    public double WeightPenaltyKg { get; set; }
    public double PowerAdjustPct { get; set; }
    public double MaxPctFuelFill { get; set; }
    public int MaxDryTireSets { get; set; }
}
