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

    public Season Season { get; set; } = null!;
    public Week? Week { get; set; }
    public Track Track { get; set; } = null!;
    public ICollection<SubsessionResult> Results { get; set; } = [];
}
