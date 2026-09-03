namespace ApexRacers.Core.Models;

public class Track
{
    public int Id { get; set; }          // iRacing track_id
    public required string Name { get; set; }        // track_name
    public string ConfigName { get; set; } = "";     // canonical storage value is empty when absent
    public int? CategoryId { get; set; }             // category_id
    public string? Category { get; set; }            // e.g. "road", "oval"
    public double? TrackConfigLength { get; set; }   // miles
    public bool IsDirt { get; set; }
    public bool IsOval { get; set; }
    public string? Location { get; set; }
    public string? TimeZone { get; set; }
    public bool Retired { get; set; }

    // Catalog-explorer enrichment (3.5) — populated by the ingestion worker + seeder.
    public int? CornersPerLap { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? PitRoadSpeedLimit { get; set; }
    public int? NumberPitstalls { get; set; }
    public bool NightLighting { get; set; }
    public bool HasSvgMap { get; set; }
    public string? AssetFolder { get; set; }      // e.g. "/img/tracks/limerockpark"
    public string? SmallImageFile { get; set; }   // e.g. "limerockpark-small.jpg"
    public string? LargeImageFile { get; set; }
    public string? TrackMapUrl { get; set; }      // already an absolute URL from iRacing

    public ICollection<Week> Weeks { get; set; } = [];
    public ICollection<UploadedLap> UploadedLaps { get; set; } = [];
}
