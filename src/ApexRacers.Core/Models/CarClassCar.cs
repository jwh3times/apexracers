namespace ApexRacers.Core.Models;

public class CarClassCar
{
    public int CarClassId { get; set; }
    public int CarId { get; set; }

    public CarClass CarClass { get; set; } = null!;
    public Car Car { get; set; } = null!;
}
