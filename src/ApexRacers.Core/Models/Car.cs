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

    // Catalog-explorer enrichment (3.5) — populated by the ingestion worker + seeder.
    public string? CarMake { get; set; }
    public string? CarModel { get; set; }
    public bool RainEnabled { get; set; }
    public string? CategoriesJson { get; set; }  // JSON array of category slugs
    public string? CarTypesJson { get; set; }     // JSON array of car-type slugs
    public string? AssetFolder { get; set; }      // e.g. "/img/cars/audir8"
    public string? SmallImageFile { get; set; }   // e.g. "audir8-small.jpg"
    public string? LargeImageFile { get; set; }
    public string? LogoPath { get; set; }         // site-relative, e.g. "/img/logos/.../x.png"

    public ICollection<SeasonCar> SeasonCars { get; set; } = [];
    public ICollection<CarClassCar> CarClassCars { get; set; } = [];
    public ICollection<CarPercentileResult> CarPercentileResults { get; set; } = [];
    public ICollection<UploadedLap> UploadedLaps { get; set; } = [];
}
