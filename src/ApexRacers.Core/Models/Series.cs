namespace ApexRacers.Core.Models;

public class Series
{
    public int Id { get; set; } // iRacing SeriesId
    public required string Name { get; set; }

    public ICollection<Season> Seasons { get; set; } = [];
}
