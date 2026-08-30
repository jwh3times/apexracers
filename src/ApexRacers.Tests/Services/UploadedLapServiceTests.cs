using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class UploadedLapServiceTests
{
    private static (ApplicationUser user, Car car, Track track) SeedUserCarAndTrack(AppDbContext db)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), IRacingCustomerId = 1, DisplayName = "Jerry" };
        var car = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        var track = new Track { Id = 1, Name = "Spa", ConfigName = "Full" };
        db.Users.Add(user);
        db.Cars.Add(car);
        db.Tracks.Add(track);
        return (user, car, track);
    }

    private static UploadedLap MakeLap(ApplicationUser user, Car car, Track track, double lapTime, int daysAgo = 0) =>
        new()
        {
            UserId = user.Id,
            CarId = car.Id,
            TrackId = track.Id,
            Track = track,
            LapTimeSeconds = lapTime,
            RecordedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
            Car = car,
        };

    [Fact]
    public async Task GetUploadedBestsAsync_NoLaps_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();
        var (user, _, _) = SeedUserCarAndTrack(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UploadedLapService(db).GetUploadedBestsAsync(user.Id, TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUploadedBestsAsync_MultipleLapsSameCarTrack_ReturnsBestLapAndCount()
    {
        await using var db = DbContextFactory.Create();
        var (user, car, track) = SeedUserCarAndTrack(db);
        db.UploadedLaps.AddRange(
            MakeLap(user, car, track, lapTime: 70),
            MakeLap(user, car, track, lapTime: 60),
            MakeLap(user, car, track, lapTime: 80));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UploadedLapService(db).GetUploadedBestsAsync(user.Id, TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(60, dto.BestLapSeconds);
        Assert.Equal(3, dto.LapCount);
    }

    [Fact]
    public async Task GetUploadedBestsAsync_TwoDifferentCarTrackCombos_OrderedByMostRecentFirst()
    {
        await using var db = DbContextFactory.Create();
        var (user, car, track) = SeedUserCarAndTrack(db);
        var car2 = new Car { Id = 2, Name = "Ferrari 296 GT3", NameAbbreviated = "F296" };
        var track2 = new Track { Id = 2, Name = "Monza", ConfigName = "Full" };
        db.Cars.Add(car2);
        db.Tracks.Add(track2);
        db.UploadedLaps.AddRange(
            MakeLap(user, car, track, lapTime: 70, daysAgo: 7),
            new UploadedLap
            {
                UserId = user.Id, CarId = 2, TrackId = 2, Track = track2,
                LapTimeSeconds = 55,
                RecordedAt = DateTimeOffset.UtcNow.AddDays(-1),
                Car = car2,
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UploadedLapService(db).GetUploadedBestsAsync(user.Id, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].CarId); // Ferrari lap recorded 1 day ago
        Assert.Equal(1, result[1].CarId);
    }
}
