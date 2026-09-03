using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class CarCatalogServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Car MakeCar(
        int id,
        string name,
        string? folder = null,
        string? small = null,
        bool? retired = null) => new()
    {
        Id = id,
        Name = name,
        NameAbbreviated = name,
        CategoriesJson = """["road"]""",
        AssetFolder = folder,
        SmallImageFile = small,
        Retired = retired,
    };

    [Fact]
    public async Task ListAsync_ReturnsAllCarsOrderedByName_WithImages()
    {
        await using var db = DbContextFactory.Create();
        db.Cars.AddRange(
            MakeCar(2, "Zonda"),
            MakeCar(1, "Audi", "/img/cars/audi", "audi-small.jpg"));
        await db.SaveChangesAsync(Ct);
        var service = new CarCatalogService(db);

        var result = await service.ListAsync(Ct);

        Assert.Equal(["Audi", "Zonda"], result.Select(c => c.Name).ToArray());
        Assert.Equal(
            "https://images-static.iracing.com/img/cars/audi/audi-small.jpg",
            result.Single(c => c.CarId == 1).SmallImageUrl);
        Assert.Null(result.Single(c => c.CarId == 2).SmallImageUrl);
    }

    [Fact]
    public async Task ListAsync_HidesOnlyRetiredCars()
    {
        await using var db = DbContextFactory.Create();
        db.Cars.AddRange(
            MakeCar(1, "Current with unknown status"),
            MakeCar(2, "Current", retired: false),
            MakeCar(3, "Retired", retired: true));
        await db.SaveChangesAsync(Ct);
        var service = new CarCatalogService(db);

        var result = await service.ListAsync(Ct);

        Assert.Equal([1, 2], result.Select(c => c.CarId).Order().ToArray());
    }

    [Fact]
    public async Task GetAsync_RetiredCar_ReturnsDetailWithCarClassesAndHistoricalUploadedBests()
    {
        await using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid();
        db.Cars.Add(MakeCar(132, "Merc GT3", "/img/cars/merc", "merc-large.jpg", retired: true));
        db.CarClasses.Add(new CarClass { Id = 2523, Name = "GT3 Class", ShortName = "GT3" });
        db.CarClassCars.Add(new CarClassCar { CarClassId = 2523, CarId = 132 });
        db.Tracks.Add(new Track { Id = 18, Name = "Spa", ConfigName = "GP" });
        db.UploadedLaps.Add(new UploadedLap
        {
            Id = Guid.NewGuid(), UserId = userId, CarId = 132, TrackId = 18,
            LapTimeSeconds = 138.5, RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
        var service = new CarCatalogService(db);

        var detail = await service.GetAsync(132, userId, Ct);

        Assert.Equal(132, detail.CarId);
        Assert.Equal("Merc GT3", detail.Name);
        Assert.Equal(2523, Assert.Single(detail.CarClasses).CarClassId);
        var best = Assert.Single(detail.YourUploadedBests);
        Assert.Equal("Spa", best.TrackName);
        Assert.Equal(138.5, best.BestLapSeconds, precision: 3);
    }

    [Fact]
    public async Task GetAsync_NoUserId_EmptyBests()
    {
        await using var db = DbContextFactory.Create();
        db.Cars.Add(MakeCar(132, "Merc GT3"));
        await db.SaveChangesAsync(Ct);
        var service = new CarCatalogService(db);

        var detail = await service.GetAsync(132, null, Ct);

        Assert.Empty(detail.YourUploadedBests);
    }

    [Fact]
    public async Task GetAsync_UnknownCar_Throws()
    {
        await using var db = DbContextFactory.Create();
        var service = new CarCatalogService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(999, null, Ct));
    }
}
