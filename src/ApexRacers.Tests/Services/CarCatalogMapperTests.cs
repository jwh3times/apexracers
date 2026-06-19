using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using Xunit;

namespace ApexRacers.Tests.Services;

public class CarCatalogMapperTests
{
    private static Car SampleCar() => new()
    {
        Id = 132,
        Name = "Mercedes-AMG GT3 2020",
        NameAbbreviated = "MercedesAMGGT3",
        CarMake = "Mercedes-AMG",
        CarModel = "GT3",
        Hp = 550,
        CarWeight = 1300,
        RainEnabled = true,
        FreeWithSubscription = false,
        CategoriesJson = """["road","sports_car"]""",
        CarTypesJson = """["road","sportscar"]""",
        AssetFolder = "/img/cars/mercedesamggt3",
        SmallImageFile = "mercedesamggt3-small.jpg",
        LargeImageFile = "mercedesamggt3-large.jpg",
        LogoPath = "/img/logos/partners/mercedes-logo.png",
    };

    [Fact]
    public void ToItem_MapsCoreFieldsAndSmallImageUrl()
    {
        var item = CarCatalogMapper.ToItem(SampleCar());

        Assert.Equal(132, item.CarId);
        Assert.Equal("Mercedes-AMG GT3 2020", item.Name);
        Assert.Equal("Mercedes-AMG", item.Make);
        Assert.Equal("GT3", item.Model);
        Assert.Equal(550, item.Hp);
        Assert.Equal(1300, item.Weight);
        Assert.True(item.RainEnabled);
        Assert.False(item.FreeWithSubscription);
        Assert.Equal(["road", "sports_car"], item.Categories);
        Assert.Equal(
            "https://images-static.iracing.com/img/cars/mercedesamggt3/mercedesamggt3-small.jpg",
            item.SmallImageUrl);
    }

    [Fact]
    public void ToItem_NoAssetFolder_NullImage()
    {
        var car = SampleCar();
        car.AssetFolder = null;
        car.SmallImageFile = null;

        var item = CarCatalogMapper.ToItem(car);

        Assert.Null(item.SmallImageUrl);
        Assert.Equal("Mercedes-AMG GT3 2020", item.Name);
    }

    [Fact]
    public void ToItem_NullCategoriesJson_EmptyList_AndNullFreeWithSub_False()
    {
        var car = SampleCar();
        car.CategoriesJson = null;
        car.FreeWithSubscription = null;

        var item = CarCatalogMapper.ToItem(car);

        Assert.Empty(item.Categories);
        Assert.False(item.FreeWithSubscription);
    }

    [Fact]
    public void ToDetail_MapsImagesCarTypesAndPassesThroughClassesAndBests()
    {
        var classes = new List<CarClassRefDto> { new(2523, "GT3 Class") };
        var bests = new List<PersonalLapDto>
        {
            new(132, "Mercedes-AMG GT3 2020", "Spa", "Grand Prix", 138.5, 3, DateTimeOffset.UtcNow),
        };

        var detail = CarCatalogMapper.ToDetail(SampleCar(), classes, bests);

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
}
