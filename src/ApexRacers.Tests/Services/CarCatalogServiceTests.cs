using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Cars;
using Aydsko.iRacingData.Common;
using NSubstitute;
using Xunit;
using CarClass = ApexRacers.Core.Models.CarClass;
using Track = ApexRacers.Core.Models.Track;

namespace ApexRacers.Tests.Services;

public class CarCatalogServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubServiceProvider(object? service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => service;
    }

    private static CarInfo Car(int id, string name) => new()
    {
        CarId = id,
        CarName = name,
        CarNameAbbreviated = name,
        Categories = ["road"],
    };

    private static CarAssetDetail Asset(int id) => new()
    {
        CarId = id,
        Folder = $"/img/cars/car{id}",
        SmallImage = $"car{id}-small.jpg",
        LargeImage = $"car{id}-large.jpg",
    };

    private static (CarCatalogService Service, IDataClient Client, AppDbContext Db) Build(
        CarInfo[] cars, Dictionary<string, CarAssetDetail> assets)
    {
        var client = Substitute.For<IDataClient>();
        client.GetCarsAsync(Arg.Any<CancellationToken>())
            .Returns(new DataResponse<CarInfo[]> { Data = cars });
        client.GetCarAssetDetailsAsync(Arg.Any<CancellationToken>())
            .Returns(new DataResponse<IReadOnlyDictionary<string, CarAssetDetail>> { Data = assets });

        var db = DbContextFactory.Create();
        var cached = new CachedIRacingClient(db, new StubServiceProvider(client));
        return (new CarCatalogService(cached, db), client, db);
    }

    [Fact]
    public async Task ListAsync_MapsCarsWithAssets_OrderedByName()
    {
        var (service, _, db) = Build(
            [Car(2, "Zonda"), Car(1, "Audi")],
            new Dictionary<string, CarAssetDetail> { ["1"] = Asset(1) });
        await using var _db = db;

        var result = await service.ListAsync(Ct);

        Assert.Equal(["Audi", "Zonda"], result.Select(c => c.Name).ToArray());
        Assert.Equal(
            "https://images-static.iracing.com/img/cars/car1/car1-small.jpg",
            result.Single(c => c.CarId == 1).SmallImageUrl);
        Assert.Null(result.Single(c => c.CarId == 2).SmallImageUrl); // no asset
    }

    [Fact]
    public async Task ListAsync_SecondCall_ServedFromCache_FetchesOnce()
    {
        var (service, client, db) = Build([Car(1, "Audi")], []);
        await using var _db = db;

        await service.ListAsync(Ct);
        await service.ListAsync(Ct);

        await client.Received(1).GetCarsAsync(Arg.Any<CancellationToken>());
        await client.Received(1).GetCarAssetDetailsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ReturnsDetailWithCarClassesAndPersonalBests()
    {
        var (service, _, db) = Build([Car(132, "Merc GT3")], new() { ["132"] = Asset(132) });
        await using var _db = db;
        var userId = Guid.NewGuid();
        db.Cars.Add(new Car { Id = 132, Name = "Merc GT3", NameAbbreviated = "Merc" });
        db.CarClasses.Add(new CarClass { Id = 2523, Name = "GT3 Class", ShortName = "GT3" });
        db.CarClassCars.Add(new CarClassCar { CarClassId = 2523, CarId = 132 });
        db.Tracks.Add(new Track { Id = 18, Name = "Spa", ConfigName = "GP" });
        db.PersonalLaps.Add(new PersonalLap
        {
            Id = Guid.NewGuid(), UserId = userId, CarId = 132, TrackId = 18,
            LapTimeSeconds = 138.5, IsValidLap = true, RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var detail = await service.GetAsync(132, userId, Ct);

        Assert.Equal(132, detail.CarId);
        Assert.Equal("Merc GT3", detail.Name);
        Assert.Contains(
            "car132-large.jpg",
            detail.LargeImageUrl);
        Assert.Equal(2523, Assert.Single(detail.CarClasses).CarClassId);
        var best = Assert.Single(detail.YourBestLaps);
        Assert.Equal("Spa", best.TrackName);
        Assert.Equal(138.5, best.BestLapSeconds, precision: 3);
    }

    [Fact]
    public async Task GetAsync_NoUserId_EmptyBests()
    {
        var (service, _, db) = Build([Car(132, "Merc GT3")], []);
        await using var _db = db;

        var detail = await service.GetAsync(132, null, Ct);

        Assert.Empty(detail.YourBestLaps);
    }

    [Fact]
    public async Task GetAsync_UnknownCar_Throws()
    {
        var (service, _, db) = Build([Car(1, "Audi")], []);
        await using var _db = db;

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(999, null, Ct));
    }
}
