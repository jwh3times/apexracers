namespace ApexRacers.Core.Models;

public class SeasonCarClass
{
    public int SeasonId { get; set; }
    public int CarClassId { get; set; }

    public Season Season { get; set; } = null!;
    public CarClass CarClass { get; set; } = null!;
}
