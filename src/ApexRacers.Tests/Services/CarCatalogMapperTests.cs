using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using Aydsko.iRacingData.Cars;
using Xunit;

namespace ApexRacers.Tests.Services;

public class CarCatalogMapperTests
{
    private static CarInfo Car() => new()
    {
        CarId = 132,
        CarName = "Mercedes-AMG GT3 2020",
        CarNameAbbreviated = "MercedesAMGGT3",
        CarMake = "Mercedes-AMG",
        CarModel = "GT3",
        Hp = 550,
        CarWeight = 1300,
        RainEnabled = true,
        FreeWithSubscription = false,
        Categories = ["road", "sports_car"],
        CarTypes = [new CarTypes { CarType = "road" }, new CarTypes { CarType = "sportscar" }],
    };

    private static CarAssetDetail Asset() => new()
    {
        CarId = 132,
        Folder = "/img/cars/mercedesamggt3",
        SmallImage = "mercedesamggt3-small.jpg",
        LargeImage = "mercedesamggt3-large.jpg",
        Logo = "/img/logos/partners/mercedes-logo.png",
    };

    [Fact]
    public void ToItem_MapsCoreFields()
    {
        var item = CarCatalogMapper.ToItem(Car(), Asset());

        Assert.Equal(132, item.CarId);
        Assert.Equal("Mercedes-AMG GT3 2020", item.Name);
        Assert.Equal("MercedesAMGGT3", item.NameAbbreviated);
        Assert.Equal("Mercedes-AMG", item.Make);
        Assert.Equal("GT3", item.Model);
        Assert.Equal(550, item.Hp);
        Assert.Equal(1300, item.Weight);
        Assert.True(item.RainEnabled);
        Assert.False(item.FreeWithSubscription);
        Assert.Equal(["road", "sports_car"], item.Categories);
    }

    [Fact]
    public void ToItem_BuildsSmallImageUrlFromFolderAndFile()
    {
        var item = CarCatalogMapper.ToItem(Car(), Asset());

        Assert.Equal(
            "https://images-static.iracing.com/img/cars/mercedesamggt3/mercedesamggt3-small.jpg",
            item.SmallImageUrl);
    }

    [Fact]
    public void ToItem_NullAsset_NullImage_StillMapsCar()
    {
        var item = CarCatalogMapper.ToItem(Car(), null);

        Assert.Null(item.SmallImageUrl);
        Assert.Equal("Mercedes-AMG GT3 2020", item.Name);
    }

    [Fact]
    public void ToItem_NullCategories_YieldsEmptyList()
    {
        var car = Car();
        car.Categories = null!;

        var item = CarCatalogMapper.ToItem(car, Asset());

        Assert.Empty(item.Categories);
    }

    [Fact]
    public void ToDetail_MapsImagesCarTypesAndPassesThroughClassesAndBests()
    {
        var classes = new List<CarClassRefDto> { new(2523, "GT3 Class") };
        var bests = new List<PersonalLapDto>
        {
            new(132, "Mercedes-AMG GT3 2020", "Spa", "Grand Prix", 138.5, 3, DateTimeOffset.UtcNow),
        };

        var detail = CarCatalogMapper.ToDetail(Car(), Asset(), classes, bests);

        Assert.Equal(
            "https://images-static.iracing.com/img/cars/mercedesamggt3/mercedesamggt3-large.jpg",
            detail.LargeImageUrl);
        Assert.Equal(
            "https://images-static.iracing.com/img/logos/partners/mercedes-logo.png",
            detail.LogoUrl);
        Assert.Equal(["road", "sportscar"], detail.CarTypes);
        Assert.Equal(2523, Assert.Single(detail.CarClasses).CarClassId);
        Assert.Equal(132, Assert.Single(detail.YourBestLaps).CarId);
    }

    [Fact]
    public void ToDetail_NullAsset_NullImagesAndUrls()
    {
        var detail = CarCatalogMapper.ToDetail(Car(), null, [], []);

        Assert.Null(detail.SmallImageUrl);
        Assert.Null(detail.LargeImageUrl);
        Assert.Null(detail.LogoUrl);
    }
}
