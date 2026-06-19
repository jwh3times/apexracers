using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Common;
using Aydsko.iRacingData.Tracks;
using NSubstitute;
using Xunit;
using Track = ApexRacers.Core.Models.Track;
using SdkTrack = Aydsko.iRacingData.Tracks.Track;

namespace ApexRacers.Tests.Services;

public class TrackCatalogServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubServiceProvider(object? service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => service;
    }

    private static SdkTrack Sdk(int id, string name, string config = "") => new()
    {
        TrackId = id,
        TrackName = name,
        ConfigName = config,
        Category = "road",
        TrackConfigLength = 2.5m,
        CornersPerLap = 10,
    };

    private static TrackAssets Asset(int id) => new()
    {
        TrackId = id,
        Folder = $"/img/tracks/track{id}",
        SmallImage = $"track{id}-small.jpg",
        LargeImage = $"track{id}-large.jpg",
    };

    private static (TrackCatalogService Service, IDataClient Client, AppDbContext Db) Build(
        SdkTrack[] tracks, Dictionary<string, TrackAssets> assets)
    {
        var client = Substitute.For<IDataClient>();
        client.GetTracksAsync(Arg.Any<CancellationToken>())
            .Returns(new DataResponse<SdkTrack[]> { Data = tracks });
        client.GetTrackAssetsAsync(Arg.Any<CancellationToken>())
            .Returns(new DataResponse<IReadOnlyDictionary<string, TrackAssets>> { Data = assets });

        var db = DbContextFactory.Create();
        var cached = new CachedIRacingClient(db, new StubServiceProvider(client));
        return (new TrackCatalogService(cached, db), client, db);
    }

    [Fact]
    public async Task ListAsync_MapsTracksWithAssets_OrderedByName()
    {
        var (service, _, db) = Build(
            [Sdk(2, "Watkins Glen"), Sdk(1, "Algarve")],
            new Dictionary<string, TrackAssets> { ["1"] = Asset(1) });
        await using var _db = db;

        var result = await service.ListAsync(Ct);

        Assert.Equal(["Algarve", "Watkins Glen"], result.Select(t => t.Name).ToArray());
        Assert.Equal(
            "https://images-static.iracing.com/img/tracks/track1/track1-small.jpg",
            result.Single(t => t.TrackId == 1).SmallImageUrl);
        Assert.Null(result.Single(t => t.TrackId == 2).SmallImageUrl);
    }

    [Fact]
    public async Task ListAsync_SecondCall_ServedFromCache_FetchesOnce()
    {
        var (service, client, db) = Build([Sdk(1, "Algarve")], []);
        await using var _db = db;

        await service.ListAsync(Ct);
        await service.ListAsync(Ct);

        await client.Received(1).GetTracksAsync(Arg.Any<CancellationToken>());
        await client.Received(1).GetTrackAssetsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ReturnsDetailWithPersonalBests()
    {
        var (service, _, db) = Build([Sdk(18, "Spa", "GP")], new() { ["18"] = Asset(18) });
        await using var _db = db;
        var userId = Guid.NewGuid();
        db.Cars.Add(new Car { Id = 132, Name = "Merc GT3", NameAbbreviated = "Merc" });
        db.Tracks.Add(new Track { Id = 18, Name = "Spa", ConfigName = "GP" });
        db.PersonalLaps.Add(new PersonalLap
        {
            Id = Guid.NewGuid(), UserId = userId, CarId = 132, TrackId = 18,
            LapTimeSeconds = 138.5, IsValidLap = true, RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var detail = await service.GetAsync(18, userId, Ct);

        Assert.Equal(18, detail.TrackId);
        Assert.Equal("Spa", detail.Name);
        Assert.Contains("track18-large.jpg", detail.LargeImageUrl);
        var best = Assert.Single(detail.YourBestLaps);
        Assert.Equal("Merc GT3", best.CarName);
        Assert.Equal(138.5, best.BestLapSeconds, precision: 3);
    }

    [Fact]
    public async Task GetAsync_NoUserId_EmptyBests()
    {
        var (service, _, db) = Build([Sdk(18, "Spa")], []);
        await using var _db = db;

        var detail = await service.GetAsync(18, null, Ct);

        Assert.Empty(detail.YourBestLaps);
    }

    [Fact]
    public async Task GetAsync_UnknownTrack_Throws()
    {
        var (service, _, db) = Build([Sdk(1, "Algarve")], []);
        await using var _db = db;

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(999, null, Ct));
    }
}
