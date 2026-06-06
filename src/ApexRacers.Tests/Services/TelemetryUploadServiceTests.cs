using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class TelemetryUploadServiceTests
{
    [Fact]
    public async Task ProcessAsync_ValidStream_ReturnsSummaryWithCorrectCounts()
    {
        await using var db = DbContextFactory.Create();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 2, lapTime: 90.5f, validLaps: true);
        var result = await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalLaps);
        Assert.Equal(2, result.ValidLaps);
        Assert.Equal(90.5, result.BestLapSeconds!.Value, tolerance: 0.005);
        Assert.Equal("Spa-Francorchamps", result.TrackName);
        Assert.Equal("Porsche 992 GT3",   result.CarName);
        Assert.Equal(12345L,              result.CustomerId);
        Assert.Equal("Jerry Holland",     result.DriverName);
    }

    [Fact]
    public async Task ProcessAsync_AllInvalidLaps_ReturnsZeroValidAndNullBest()
    {
        await using var db = DbContextFactory.Create();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 2, validLaps: false);
        var result = await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalLaps);
        Assert.Equal(0, result.ValidLaps);
        Assert.Null(result.BestLapSeconds);
    }

    [Fact]
    public async Task ProcessAsync_CarNotInDb_UpsertsCar()
    {
        await using var db = DbContextFactory.Create();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 1, carId: 99);
        await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var car = db.Cars.Single();
        Assert.Equal(99, car.Id);
        Assert.Equal("Porsche 992 GT3", car.Name);
    }

    [Fact]
    public async Task ProcessAsync_CarAlreadyInDb_DoesNotDuplicateCar()
    {
        await using var db = DbContextFactory.Create();
        db.Cars.Add(new Car { Id = 99, Name = "Porsche 992 GT3", NameAbbreviated = "P992" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var svc = new TelemetryUploadService(db);
        using var stream = FakeIbtBuilder.Build(laps: 1, carId: 99);
        await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Single(db.Cars);
    }

    [Fact]
    public async Task ProcessAsync_ValidLaps_SavesEachLapAsPersonalLap()
    {
        await using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 3, lapTime: 95.0f, validLaps: true);
        await svc.ProcessAsync(stream, userId, TestContext.Current.CancellationToken);

        var laps = db.PersonalLaps.ToList();
        Assert.Equal(3, laps.Count);
        Assert.All(laps, l =>
        {
            Assert.Equal(userId, l.UserId);
            Assert.True(l.IsValidLap);
            Assert.Equal(95.0, l.LapTimeSeconds, tolerance: 0.005);
            Assert.Equal(LapSessionType.Unknown, l.SessionType);
        });
    }

    [Fact]
    public async Task ProcessAsync_ValidLaps_SavesSessionTypeFromFile()
    {
        await using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 1, lapTime: 90.0f, validLaps: true, eventType: LapSessionType.Race);
        await svc.ProcessAsync(stream, userId, TestContext.Current.CancellationToken);

        var lap = Assert.Single(db.PersonalLaps);
        Assert.Equal(LapSessionType.Race, lap.SessionType);
    }

    [Fact]
    public async Task ProcessAsync_InvalidLaps_SavesNoPersonalLaps()
    {
        await using var db = DbContextFactory.Create();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 2, validLaps: false);
        await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Empty(db.PersonalLaps);
    }
}
