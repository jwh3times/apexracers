namespace ApexRacers.Core.Models;

public class Season
{
    public int Id { get; set; } // iRacing SeasonId
    public int SeriesId { get; set; }
    public int Year { get; set; }
    public int Quarter { get; set; }
    public bool Active { get; set; }

    public Series Series { get; set; } = null!;
    public ICollection<Week> Weeks { get; set; } = [];
    public ICollection<SeasonCar> SeasonCars { get; set; } = [];
}
