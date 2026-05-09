namespace ApexRacers.Core.Models;

public class SeasonCar
{
    public int SeasonId { get; set; }
    public int CarId { get; set; }

    public Season Season { get; set; } = null!;
    public Car Car { get; set; } = null!;
}
