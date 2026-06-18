namespace ApexRacers.Core.Models;

public class Week
{
    public Guid Id { get; set; }
    public int SeasonId { get; set; }
    public int WeekNumber { get; set; }
    public int TrackId { get; set; }
    public DateOnly StartDate { get; set; }
    public string? WeatherSummaryJson { get; set; }   // serialized schedule weather_summary (2.1)

    public Season Season { get; set; } = null!;
    public Track Track { get; set; } = null!;
    public ICollection<Subsession> Subsessions { get; set; } = [];
    public ICollection<CarPercentileResult> CarPercentileResults { get; set; } = [];
}
