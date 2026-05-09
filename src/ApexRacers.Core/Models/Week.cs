namespace ApexRacers.Core.Models;

public class Week
{
    public int Id { get; set; }
    public int SeasonId { get; set; }
    public int WeekNumber { get; set; }
    public int IracingTrackId { get; set; }
    public required string TrackName { get; set; }
    public required string ConfigName { get; set; }
    public DateOnly StartDate { get; set; }

    public Season Season { get; set; } = null!;
    public ICollection<LapTimeEntry> LapTimeEntries { get; set; } = [];
    public ICollection<CarPercentileResult> CarPercentileResults { get; set; } = [];
}
