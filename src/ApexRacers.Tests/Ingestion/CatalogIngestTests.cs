using ApexRacers.Ingestion;
using Aydsko.iRacingData.Cars;
using Aydsko.iRacingData.Tracks;
using Xunit;
using CoreCar = ApexRacers.Core.Models.Car;
using CoreTrack = ApexRacers.Core.Models.Track;

namespace ApexRacers.Tests.Ingestion;

public class CatalogIngestTests
{
    [Fact]
    public void PopulateCar_MapsSpecsCategoriesAndAssetBits()
    {
        var car = new CoreCar { Id = 132, Name = "", NameAbbreviated = "" };
        var info = new CarInfo
        {
            CarId = 132,
            CarName = "Merc GT3",
            CarNameAbbreviated = "Merc",
            CarMake = "Mercedes",
            CarModel = "GT3",
            Hp = 550,
            CarWeight = 1300,
            RainEnabled = true,
            FreeWithSubscription = false,
            Categories = ["road", "sports_car"],
            CarTypes = [new CarTypes { CarType = "road" }],
        };
        var asset = new CarAssetDetail
        {
            CarId = 132,
            Folder = "/img/cars/merc",
            SmallImage = "merc-small.jpg",
            LargeImage = "merc-large.jpg",
            Logo = "/img/logos/merc.png",
        };

        CatalogIngest.PopulateCar(car, info, asset);

        Assert.Equal("Merc GT3", car.Name);
        Assert.Equal("Mercedes", car.CarMake);
        Assert.Equal(550, car.Hp);
        Assert.True(car.RainEnabled);
        Assert.Equal("""["road","sports_car"]""", car.CategoriesJson);
        Assert.Equal("""["road"]""", car.CarTypesJson);
        Assert.Equal("/img/cars/merc", car.AssetFolder);
        Assert.Equal("merc-small.jpg", car.SmallImageFile);
        Assert.Equal("/img/logos/merc.png", car.LogoPath);
    }

    [Fact]
    public void PopulateCar_NullAssetAndCategories_NullBits()
    {
        var car = new CoreCar { Id = 1, Name = "", NameAbbreviated = "" };

        CatalogIngest.PopulateCar(
            car, new CarInfo { CarId = 1, CarName = "X", CarNameAbbreviated = "X" }, null);

        Assert.Null(car.AssetFolder);
        Assert.Null(car.SmallImageFile);
        Assert.Null(car.CategoriesJson);
        Assert.Null(car.CarTypesJson);
    }

    [Fact]
    public void PopulateTrack_MapsSpecsGeoAndAssetBits()
    {
        var track = new CoreTrack { Id = 18, Name = "" };
        var info = new Track
        {
            TrackId = 18,
            TrackName = "Spa",
            ConfigName = "GP",
            Category = "road",
            TrackConfigLength = 4.35m,
            CornersPerLap = 20,
            Latitude = 50.44m,
            Longitude = 5.97m,
            PitRoadSpeedLimit = 60,
            NumberPitstalls = 40,
            NightLighting = true,
            HasSvgMap = true,
        };
        var asset = new TrackAssets
        {
            TrackId = 18,
            Folder = "/img/tracks/spa",
            SmallImage = "spa-small.jpg",
            LargeImage = "spa-large.jpg",
            TrackMap = "https://map/spa/",
        };

        CatalogIngest.PopulateTrack(track, info, asset);

        Assert.Equal("Spa", track.Name);
        Assert.Equal(20, track.CornersPerLap);
        Assert.Equal(50.44, track.Latitude!.Value, precision: 2);
        Assert.Equal(60, track.PitRoadSpeedLimit);
        Assert.True(track.NightLighting);
        Assert.True(track.HasSvgMap);
        Assert.Equal("/img/tracks/spa", track.AssetFolder);
        Assert.Equal("https://map/spa/", track.TrackMapUrl);
    }
}
