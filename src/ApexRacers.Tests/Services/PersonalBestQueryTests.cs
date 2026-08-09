using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// Covers the projection that was previously written three times. The invariants below belonged to
/// all three copies but were asserted by none of them — no test in the suite seeded an invalid lap
/// before this file existed, so "only valid laps count" was three lines of code and zero checks.
/// </summary>
public class PersonalBestQueryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly DateTimeOffset Base = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<AppDbContext> SeedAsync(params PersonalLap[] laps)
    {
        var db = DbContextFactory.Create();
        db.Cars.AddRange(
            new Car { Id = 1, Name = "Ferrari 296", NameAbbreviated = "F296" },
            new Car { Id = 2, Name = "Porsche 911", NameAbbreviated = "P911" });
        db.Tracks.AddRange(
            new Track { Id = 10, Name = "Spa", ConfigName = "Grand Prix" },
            new Track { Id = 11, Name = "Spa", ConfigName = "Endurance" });
        db.PersonalLaps.AddRange(laps);
        await db.SaveChangesAsync(Ct);
        return db;
    }

    private static PersonalLap Lap(
        int carId, int trackId, double seconds, bool valid = true, int dayOffset = 0, Guid? userId = null) =>
        new()
        {
            UserId = userId ?? UserId,
            CarId = carId,
            TrackId = trackId,
            LapTimeSeconds = seconds,
            IsValidLap = valid,
            RecordedAt = Base.AddDays(dayOffset),
        };

    [Fact]
    public async Task ExcludesInvalidLaps_SoNoCallerCanForgetTo()
    {
        await using var db = await SeedAsync(
            Lap(1, 10, 100.0, valid: false), // faster, but invalid
            Lap(1, 10, 105.0));

        var result = await PersonalBestQuery.RunAsync(
            db.PersonalLaps.Where(l => l.UserId == UserId), PersonalBestOrder.FastestFirst, Ct);

        var row = Assert.Single(result);
        Assert.Equal(105.0, row.BestLapSeconds);
        Assert.Equal(1, row.LapCount); // the invalid lap is not counted either
    }

    [Fact]
    public async Task DropsACarTrackEntirelyWhenEveryLapIsInvalid()
    {
        await using var db = await SeedAsync(Lap(1, 10, 100.0, valid: false));

        var result = await PersonalBestQuery.RunAsync(
            db.PersonalLaps.Where(l => l.UserId == UserId), PersonalBestOrder.FastestFirst, Ct);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GroupsPerCarAndTrackConfiguration()
    {
        // Same track name, different configuration — these are separate rows, which is why the
        // group key spans ConfigName and not just the track.
        await using var db = await SeedAsync(
            Lap(1, 10, 100.0),
            Lap(1, 11, 200.0),
            Lap(2, 10, 300.0));

        var result = await PersonalBestQuery.RunAsync(
            db.PersonalLaps.Where(l => l.UserId == UserId), PersonalBestOrder.FastestFirst, Ct);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.TrackName == "Spa" && r.ConfigName == "Grand Prix" && r.CarId == 1);
        Assert.Contains(result, r => r.TrackName == "Spa" && r.ConfigName == "Endurance" && r.CarId == 1);
    }

    [Fact]
    public async Task AggregatesBestCountAndMostRecent()
    {
        await using var db = await SeedAsync(
            Lap(1, 10, 105.0, dayOffset: 0),
            Lap(1, 10, 99.5, dayOffset: 1),
            Lap(1, 10, 101.0, dayOffset: 2));

        var result = await PersonalBestQuery.RunAsync(
            db.PersonalLaps.Where(l => l.UserId == UserId), PersonalBestOrder.FastestFirst, Ct);

        var row = Assert.Single(result);
        Assert.Equal(99.5, row.BestLapSeconds); // min, not most recent
        Assert.Equal(3, row.LapCount);
        Assert.Equal(Base.AddDays(2), row.LastRecordedAt); // max, not the best lap's date
        Assert.Equal("Ferrari 296", row.CarName);
    }

    [Fact]
    public async Task RespectsTheCallersScope()
    {
        await using var db = await SeedAsync(
            Lap(1, 10, 100.0),
            Lap(2, 10, 90.0),
            Lap(1, 10, 80.0, userId: OtherUserId));

        var carScoped = await PersonalBestQuery.RunAsync(
            db.PersonalLaps.Where(l => l.UserId == UserId && l.CarId == 1),
            PersonalBestOrder.FastestFirst, Ct);

        var row = Assert.Single(carScoped);
        Assert.Equal(1, row.CarId);
        Assert.Equal(100.0, row.BestLapSeconds); // the other user's faster lap is not ours
    }

    [Fact]
    public async Task FastestFirst_OrdersByBestLap()
    {
        await using var db = await SeedAsync(
            Lap(1, 10, 300.0, dayOffset: 5),
            Lap(2, 10, 100.0, dayOffset: 0),
            Lap(1, 11, 200.0, dayOffset: 3));

        var result = await PersonalBestQuery.RunAsync(
            db.PersonalLaps.Where(l => l.UserId == UserId), PersonalBestOrder.FastestFirst, Ct);

        Assert.Equal([100.0, 200.0, 300.0], result.Select(r => r.BestLapSeconds));
    }

    [Fact]
    public async Task MostRecentFirst_OrdersByLastRecorded()
    {
        await using var db = await SeedAsync(
            Lap(1, 10, 300.0, dayOffset: 5),
            Lap(2, 10, 100.0, dayOffset: 0),
            Lap(1, 11, 200.0, dayOffset: 3));

        var result = await PersonalBestQuery.RunAsync(
            db.PersonalLaps.Where(l => l.UserId == UserId), PersonalBestOrder.MostRecentFirst, Ct);

        Assert.Equal(
            [Base.AddDays(5), Base.AddDays(3), Base],
            result.Select(r => r.LastRecordedAt));
    }

    [Fact]
    public async Task EmptyScope_ReturnsEmpty()
    {
        await using var db = await SeedAsync();

        var result = await PersonalBestQuery.RunAsync(
            db.PersonalLaps.Where(l => l.UserId == UserId), PersonalBestOrder.FastestFirst, Ct);

        Assert.Empty(result);
    }
}
