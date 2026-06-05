namespace ApexRacers.Core.Models;

public class Series
{
    public int Id { get; set; } // iRacing SeriesId
    public required string Name { get; set; }
    public int? CategoryId { get; set; }
    public string? Category { get; set; }
    public int? LicenseGroup { get; set; }
    public bool? Official { get; set; }

    public ICollection<Season> Seasons { get; set; } = [];
}
