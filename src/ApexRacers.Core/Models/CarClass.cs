namespace ApexRacers.Core.Models;

public class CarClass
{
    public int Id { get; set; } // iRacing CarClassId
    public required string Name { get; set; }
    public required string ShortName { get; set; }
    public int RelativeSpeed { get; set; }

    public ICollection<CarClassCar> CarClassCars { get; set; } = [];
    public ICollection<SeasonCarClass> SeasonCarClasses { get; set; } = [];
}
