using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// Covers the projection that was previously written three times. Every row in its scope is already
/// a Timed Lap because the upload service rejects untimed parser rows before persistence.
/// </summary>
public class UploadedBestQueryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly DateTimeOffset Base = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<AppDbContext> SeedAsync(params UploadedLap[] laps)
    {
        var db = DbContextFactory.Create();
        db.Cars.AddRange(
            new Car { Id = 1, Name = "Ferrari 296", NameAbbreviated = "F296" },
            new Car { Id = 2, Name = "Porsche 911", NameAbbreviated = "P911" });
        db.Tracks.AddRange(
            new Track { Id = 10, Name = "Spa", ConfigName = "Grand Prix" },
            new Track { Id = 11, Name = "Spa", ConfigName = "Endurance" },
            // 12 and 13 are labelled identically on purpose: iRacing happens to label every
            // configuration apart today, and the grouping must not depend on that holding.
            new Track { Id = 12, Name = "Twin Ring", ConfigName = "Full" },
            new Track { Id = 13, Name = "Twin Ring", ConfigName = "Full" });
        db.UploadedLaps.AddRange(laps);
        await db.SaveChangesAsync(Ct);
        return db;
    }

    private static UploadedLap Lap(
        int carId, int trackId, double seconds, int dayOffset = 0, Guid? userId = null) =>
        new()
        {
            UserId = userId ?? UserId,
            CarId = carId,
            TrackId = trackId,
            LapTimeSeconds = seconds,
            RecordedAt = Base.AddDays(dayOffset),
        };

    [Fact]
    public async Task GroupsPerCarAndTrackConfiguration()
    {
        // Same track name, different configuration — these are separate rows, which is why the
        // group key spans ConfigName and not just the track.
        await using var db = await SeedAsync(
            Lap(1, 10, 100.0),
            Lap(1, 11, 200.0),
            Lap(2, 10, 300.0));

        var result = await UploadedBestQuery.RunAsync(
            db.UploadedLaps.Where(l => l.UserId == UserId), UploadedBestOrder.FastestFirst, Ct);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.TrackId == 10 && r.TrackName == "Spa" && r.ConfigName == "Grand Prix" && r.CarId == 1);
        Assert.Contains(result, r => r.TrackId == 11 && r.TrackName == "Spa" && r.ConfigName == "Endurance" && r.CarId == 1);
    }

    [Fact]
    public async Task KeysOnTrackIdentity_SoIdenticallyLabelledTracksStayApart()
    {
        // Tracks 12 and 13 share a name *and* a configuration. Keyed on labels these collapse into
        // one row and report a best lap set at a track the driver never drove; keyed on identity
        // they stay two. Nothing ApexRacers controls keeps iRacing's labels distinct.
        await using var db = await SeedAsync(
            Lap(1, 12, 100.0),
            Lap(1, 13, 200.0));

        var result = await UploadedBestQuery.RunAsync(
            db.UploadedLaps.Where(l => l.UserId == UserId), UploadedBestOrder.FastestFirst, Ct);

        Assert.Equal(2, result.Count);
        Assert.Equal(100.0, result.Single(r => r.TrackId == 12).BestLapSeconds);
        Assert.Equal(200.0, result.Single(r => r.TrackId == 13).BestLapSeconds);
        Assert.All(result, r => Assert.Equal("Twin Ring", r.TrackName));
    }

    [Fact]
    public async Task CarriesTheLabelsAlongsideTheIdentifiers()
    {
        await using var db = await SeedAsync(Lap(1, 11, 100.0));

        var row = Assert.Single(await UploadedBestQuery.RunAsync(
            db.UploadedLaps.Where(l => l.UserId == UserId), UploadedBestOrder.FastestFirst, Ct));

        Assert.Equal(1, row.CarId);
        Assert.Equal("Ferrari 296", row.CarName);
        Assert.Equal(11, row.TrackId);
        Assert.Equal("Spa", row.TrackName);
        Assert.Equal("Endurance", row.ConfigName);
    }

    [Fact]
    public async Task AggregatesBestCountAndMostRecent()
    {
        await using var db = await SeedAsync(
            Lap(1, 10, 105.0, dayOffset: 0),
            Lap(1, 10, 99.5, dayOffset: 1),
            Lap(1, 10, 101.0, dayOffset: 2));

        var result = await UploadedBestQuery.RunAsync(
            db.UploadedLaps.Where(l => l.UserId == UserId), UploadedBestOrder.FastestFirst, Ct);

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

        var carScoped = await UploadedBestQuery.RunAsync(
            db.UploadedLaps.Where(l => l.UserId == UserId && l.CarId == 1),
            UploadedBestOrder.FastestFirst, Ct);

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

        var result = await UploadedBestQuery.RunAsync(
            db.UploadedLaps.Where(l => l.UserId == UserId), UploadedBestOrder.FastestFirst, Ct);

        Assert.Equal([100.0, 200.0, 300.0], result.Select(r => r.BestLapSeconds));
    }

    [Fact]
    public async Task MostRecentFirst_OrdersByLastRecorded()
    {
        await using var db = await SeedAsync(
            Lap(1, 10, 300.0, dayOffset: 5),
            Lap(2, 10, 100.0, dayOffset: 0),
            Lap(1, 11, 200.0, dayOffset: 3));

        var result = await UploadedBestQuery.RunAsync(
            db.UploadedLaps.Where(l => l.UserId == UserId), UploadedBestOrder.MostRecentFirst, Ct);

        Assert.Equal(
            [Base.AddDays(5), Base.AddDays(3), Base],
            result.Select(r => r.LastRecordedAt));
    }

    [Fact]
    public async Task EmptyScope_ReturnsEmpty()
    {
        await using var db = await SeedAsync();

        var result = await UploadedBestQuery.RunAsync(
            db.UploadedLaps.Where(l => l.UserId == UserId), UploadedBestOrder.FastestFirst, Ct);

        Assert.Empty(result);
    }
}
