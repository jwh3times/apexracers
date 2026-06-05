namespace ApexRacers.Core.Models;

public class Car
{
    public int Id { get; set; } // iRacing CarId
    public required string Name { get; set; }
    public required string NameAbbreviated { get; set; }
    public bool? Retired { get; set; }
    public bool? FreeWithSubscription { get; set; }
    public int? PackageId { get; set; }
    public int? Hp { get; set; }
    public int? CarWeight { get; set; }

    public ICollection<SeasonCar> SeasonCars { get; set; } = [];
    public ICollection<CarClassCar> CarClassCars { get; set; } = [];
    public ICollection<CarPercentileResult> CarPercentileResults { get; set; } = [];
    public ICollection<PersonalLap> PersonalLaps { get; set; } = [];
}
