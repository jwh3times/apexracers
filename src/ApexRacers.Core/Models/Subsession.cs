namespace ApexRacers.Core.Models;

public class Subsession
{
    public int Id { get; set; }                    // subsession_id
    public int SeasonId { get; set; }
    public int WeekNumber { get; set; }            // race_week_num (0-based)
    public Guid? WeekId { get; set; }              // FK → Week (nullable)
    public int TrackId { get; set; }               // FK → Track
    public bool OfficialSession { get; set; }
    public int EventStrengthOfField { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public int SplitNum { get; set; }              // 0 = highest SOF split

    // Race context (1.3) — populated by the ingestion worker.
    public int NumCautions { get; set; }
    public int NumCautionLaps { get; set; }
    public int NumLeadChanges { get; set; }
    public int CornersPerLap { get; set; }
    public double EventAverageLapSeconds { get; set; }  // seconds; -1 = none
    public double EventBestLapSeconds { get; set; }     // seconds; -1 = none
    public int EventLapsComplete { get; set; }
    public string? WeatherJson { get; set; }            // serialized iRacing weather block
    public string? TrackStateJson { get; set; }         // serialized iRacing track-state block

    public Season Season { get; set; } = null!;
    public Week? Week { get; set; }
    public Track Track { get; set; } = null!;
    public ICollection<SubsessionResult> Results { get; set; } = [];
}
