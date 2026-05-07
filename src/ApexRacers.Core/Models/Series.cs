namespace ApexRacers.Core.Models;

public class Series
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int CurrentSeason { get; set; }

    public ICollection<Week> Weeks { get; set; } = [];
}
