namespace ApexRacers.Core.Models;

public class Week
{
    public Guid Id { get; set; }
    public int SeasonId { get; set; }
    public int WeekNumber { get; set; }
    public int TrackId { get; set; }
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// When this Race Week stops accepting official results, as iRacing reports it
    /// (<c>week_end_time</c>) — an exact UTC instant, typically 21:00 on the seventh day rather
    /// than midnight, so it cannot be reconstructed as <see cref="StartDate"/> plus seven days.
    ///
    /// <para>Null for a Week ingested before this column existed, and for any Week whose payload
    /// omitted the field. Null means "not established" rather than "no end": callers derive a
    /// fallback boundary instead of treating the Week as open-ended. See
    /// <c>ApexRacers.Core.RaceWeekWindow</c>, which owns that rule.</para>
    /// </summary>
    public DateTimeOffset? EndTime { get; set; }

    public string? WeatherSummaryJson { get; set; }   // serialized schedule weather_summary (2.1)

    public Season Season { get; set; } = null!;
    public Track Track { get; set; } = null!;
    public ICollection<Subsession> Subsessions { get; set; } = [];
    public ICollection<CarPercentileResult> CarPercentileResults { get; set; } = [];
}
