namespace ApexRacers.Core.Models;

public class Car
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public ICollection<LapTimeEntry> LapTimeEntries { get; set; } = [];
    public ICollection<CarPercentileResult> CarPercentileResults { get; set; } = [];
}
