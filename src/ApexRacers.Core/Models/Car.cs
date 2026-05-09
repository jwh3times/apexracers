namespace ApexRacers.Core.Models;

public class Car
{
    public int Id { get; set; } // iRacing CarId
    public required string Name { get; set; }
    public required string NameAbbreviated { get; set; }

    public ICollection<SeasonCar> SeasonCars { get; set; } = [];
    public ICollection<LapTimeEntry> LapTimeEntries { get; set; } = [];
    public ICollection<CarPercentileResult> CarPercentileResults { get; set; } = [];
    public ICollection<PersonalLap> PersonalLaps { get; set; } = [];
}
