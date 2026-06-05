namespace ApexRacers.Core.Models;

public class Season
{
    public int Id { get; set; } // iRacing SeasonId
    public int SeriesId { get; set; }
    public int Year { get; set; }
    public int Quarter { get; set; }
    public bool Active { get; set; }
    public int? LicenseGroup { get; set; }
    public bool? Official { get; set; }
    public int? Drops { get; set; }
    public bool? FixedSetup { get; set; }
    public bool? Multiclass { get; set; }

    public Series Series { get; set; } = null!;
    public ICollection<Week> Weeks { get; set; } = [];
    public ICollection<SeasonCar> SeasonCars { get; set; } = [];
    public ICollection<SeasonCarClass> SeasonCarClasses { get; set; } = [];
}
