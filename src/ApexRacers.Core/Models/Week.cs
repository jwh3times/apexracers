namespace ApexRacers.Core.Models;

public class Week
{
    public int Id { get; set; }
    public int SeriesId { get; set; }
    public int WeekNumber { get; set; }
    public required string TrackName { get; set; }
    public required string CarClass { get; set; }

    public Series Series { get; set; } = null!;
    public ICollection<LapTimeEntry> LapTimeEntries { get; set; } = [];
    public ICollection<CarPercentileResult> CarPercentileResults { get; set; } = [];
}
