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
    /// <summary>
    /// Zero-based Split Index within this Subsession's Race Session, ordered by Strength of Field
    /// descending — 0 is the strongest Split. Null when the Split Index is unknown, which is not the
    /// same as being the strongest Split: iRacing supplied no splits, or supplied a list this
    /// Subsession was absent from.
    /// </summary>
    public int? SplitIndex { get; set; }

    /// <summary>
    /// How many Splits the Race Session divided into. Null whenever <see cref="SplitIndex"/> is —
    /// the two are established together from one payload — so "Split 1 of 3" is never half-known.
    /// </summary>
    public int? SplitCount { get; set; }

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
