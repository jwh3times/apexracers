using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class TrackCatalogServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track MakeTrack(int id, string name, string config = "", string? folder = null, string? small = null) => new()
    {
        Id = id,
        Name = name,
        ConfigName = config,
        Category = "road",
        TrackConfigLength = 2.5,
        AssetFolder = folder,
        SmallImageFile = small,
    };

    [Fact]
    public async Task ListAsync_ReturnsAllTracksOrderedByName_WithImages()
    {
        await using var db = DbContextFactory.Create();
        db.Tracks.AddRange(
            MakeTrack(2, "Watkins Glen"),
            MakeTrack(1, "Algarve", folder: "/img/tracks/algarve", small: "algarve-small.jpg"));
        await db.SaveChangesAsync(Ct);
        var service = new TrackCatalogService(db);

        var result = await service.ListAsync(Ct);

        Assert.Equal(["Algarve", "Watkins Glen"], result.Select(t => t.Name).ToArray());
        Assert.Equal(
            "https://images-static.iracing.com/img/tracks/algarve/algarve-small.jpg",
            result.Single(t => t.TrackId == 1).SmallImageUrl);
        Assert.Null(result.Single(t => t.TrackId == 2).SmallImageUrl);
    }

    [Fact]
    public async Task GetAsync_ReturnsDetailWithPersonalBests()
    {
        await using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid();
        db.Cars.Add(new Car { Id = 132, Name = "Merc GT3", NameAbbreviated = "Merc" });
        db.Tracks.Add(MakeTrack(18, "Spa", "GP", "/img/tracks/spa", "spa-large.jpg"));
        db.UploadedLaps.Add(new UploadedLap
        {
            Id = Guid.NewGuid(), UserId = userId, CarId = 132, TrackId = 18,
            LapTimeSeconds = 138.5, IsValidLap = true, RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
        var service = new TrackCatalogService(db);

        var detail = await service.GetAsync(18, userId, Ct);

        Assert.Equal(18, detail.TrackId);
        Assert.Equal("Spa", detail.Name);
        var best = Assert.Single(detail.YourUploadedBests);
        Assert.Equal("Merc GT3", best.CarName);
        Assert.Equal(138.5, best.BestLapSeconds, precision: 3);
    }

    [Fact]
    public async Task GetAsync_NoUserId_EmptyBests()
    {
        await using var db = DbContextFactory.Create();
        db.Tracks.Add(MakeTrack(18, "Spa"));
        await db.SaveChangesAsync(Ct);
        var service = new TrackCatalogService(db);

        var detail = await service.GetAsync(18, null, Ct);

        Assert.Empty(detail.YourUploadedBests);
    }

    [Fact]
    public async Task GetAsync_UnknownTrack_Throws()
    {
        await using var db = DbContextFactory.Create();
        var service = new TrackCatalogService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(999, null, Ct));
    }
}
