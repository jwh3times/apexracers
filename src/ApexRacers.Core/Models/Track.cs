namespace ApexRacers.Core.Models;

public class Track
{
    public int Id { get; set; }          // iRacing track_id
    public required string Name { get; set; }        // track_name
    public string ConfigName { get; set; } = "";     // config_name (empty string when no config)
    public int? CategoryId { get; set; }             // category_id
    public string? Category { get; set; }            // e.g. "road", "oval"
    public double? TrackConfigLength { get; set; }   // miles
    public bool IsDirt { get; set; }
    public bool IsOval { get; set; }
    public string? Location { get; set; }
    public string? TimeZone { get; set; }
    public bool Retired { get; set; }

    public ICollection<Week> Weeks { get; set; } = [];
    public ICollection<PersonalLap> PersonalLaps { get; set; } = [];
}
