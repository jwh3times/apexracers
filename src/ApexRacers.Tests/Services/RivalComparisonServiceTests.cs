using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Common;
using Aydsko.iRacingData.Member;
using Aydsko.iRacingData.Stats;
using NSubstitute;
using Xunit;
using Track = ApexRacers.Core.Models.Track;

namespace ApexRacers.Tests.Services;

public class RivalComparisonServiceTests
{
    private const long Me = 100;
    private const long Rival = 200;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private static MemberProfile ProfileNamed(long custId, string name) => new()
    {
        CustomerId = (int)custId,
        LicenseHistory = [new LicenseHistory { CategoryId = 5, Category = "sports_car", Irating = 2000, Color = "00c702", GroupName = "Class A" }],
        Info = new MemberProfileInfo { DisplayName = name },
    };

    private static RivalComparisonService Build(AppDbContext db)
    {
        var client = Substitute.For<IDataClient>();
        client.GetMemberProfileAsync((int)Me, Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberProfile> { Data = ProfileNamed(Me, "You") });
        client.GetMemberProfileAsync((int)Rival, Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberProfile> { Data = ProfileNamed(Rival, "Rival") });
        client.GetCareerStatisticsAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberCareer> { Data = new MemberCareer { Statistics = [] } });
        client.GetMemberChartDataAsync(Arg.Any<int?>(), Arg.Any<int>(), Arg.Any<MemberChartType>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberChart> { Data = new MemberChart { CategoryId = 5, Points = [] } });

        var cached = new CachedIRacingClient(db, client);
        return new RivalComparisonService(new MemberStatsService(cached), db);
    }

    private static void SeedRace(AppDbContext db, int subId, int trackId, string trackName,
        DateTimeOffset start, string configName = "")
    {
        db.Subsessions.Add(new Subsession { Id = subId, TrackId = trackId, StartTime = start });
        if (!db.Tracks.Local.Any(t => t.Id == trackId))
            db.Tracks.Add(new Track { Id = trackId, Name = trackName, ConfigName = configName });
    }

    private static void SeedResult(AppDbContext db, int subId, long custId, int finish,
        int oldIr, int newIr, int incidents, double bestLap)
    {
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = subId,
            CustId = custId,
            FinishPosition = finish,
            OldIRating = oldIr,
            NewIRating = newIr,
            Incidents = incidents,
            BestLapSeconds = bestLap,
        });
    }

    [Fact]
    public async Task CompareAsync_AssemblesBothSidesAndSharedRaceHeadToHead()
    {
        await using var db = DbContextFactory.Create();
        SeedRace(db, 1, 10, "Spa", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        SeedRace(db, 2, 20, "Monza", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        SeedRace(db, 3, 30, "Sebring", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        // Sub 1 (Spa): I finish 2, rival 5 → I'm ahead. Sub 2 (Monza): I finish 8, rival 3 → rival ahead.
        SeedResult(db, 1, Me, finish: 2, oldIr: 2000, newIr: 2025, incidents: 1, bestLap: 120.0);
        SeedResult(db, 1, Rival, finish: 5, oldIr: 1900, newIr: 1880, incidents: 4, bestLap: 121.0);
        SeedResult(db, 2, Me, finish: 8, oldIr: 2025, newIr: 2010, incidents: 6, bestLap: 102.0);
        SeedResult(db, 2, Rival, finish: 3, oldIr: 1880, newIr: 1905, incidents: 0, bestLap: 101.5);
        SeedResult(db, 3, Me, finish: 1, oldIr: 2010, newIr: 2040, incidents: 0, bestLap: 95.0); // not shared
        await db.SaveChangesAsync(Ct);
        var service = Build(db);

        var result = await service.CompareAsync(Me, Rival, Ct);

        Assert.Equal("You", result.You.DisplayName);
        Assert.Equal("Rival", result.Rival.DisplayName);
        Assert.Equal(Me, result.You.CustId);
        Assert.Equal(Rival, result.Rival.CustId);

        Assert.Equal(2, result.Shared.TotalShared); // sub 3 not shared
        Assert.Equal(1, result.Shared.YouAhead);
        Assert.Equal(1, result.Shared.RivalAhead);

        // Newest first → Monza (sub 2) then Spa (sub 1).
        Assert.Equal([2, 1], result.Shared.Races.Select(r => r.SubsessionId).ToArray());
        var spa = result.Shared.Races.Single(r => r.SubsessionId == 1);
        Assert.Equal(2, spa.YourFinish);
        Assert.Equal(5, spa.RivalFinish);
        Assert.Equal(25, spa.YourIRatingDelta);   // 2025 - 2000
        Assert.Equal(-20, spa.RivalIRatingDelta);  // 1880 - 1900

        var spaPace = result.Shared.TrackPace.Single(p => p.TrackName == "Spa");
        Assert.Equal(120.0, spaPace.YourBestLapSeconds, precision: 3);
        Assert.Equal(121.0, spaPace.RivalBestLapSeconds, precision: 3);
    }

    [Fact]
    public async Task CompareAsync_TrackPace_SeparatesLayoutsSharingAVenueName()
    {
        await using var db = DbContextFactory.Create();
        SeedRace(db, 1, 20, "Homestead Miami Speedway",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), configName: "Oval");
        SeedRace(db, 2, 22, "Homestead Miami Speedway",
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), configName: "Road Course B");
        SeedResult(db, 1, Me, finish: 2, oldIr: 2000, newIr: 2025, incidents: 1, bestLap: 30.1);
        SeedResult(db, 1, Rival, finish: 5, oldIr: 1900, newIr: 1880, incidents: 4, bestLap: 30.4);
        SeedResult(db, 2, Me, finish: 3, oldIr: 2025, newIr: 2010, incidents: 0, bestLap: 95.2);
        SeedResult(db, 2, Rival, finish: 1, oldIr: 1880, newIr: 1905, incidents: 0, bestLap: 94.8);
        await db.SaveChangesAsync(Ct);
        var service = Build(db);

        var result = await service.CompareAsync(Me, Rival, Ct);

        Assert.Equal(2, result.Shared.TrackPace.Count);
        var oval = result.Shared.TrackPace.Single(p => p.TrackId == 20);
        Assert.Equal("Oval", oval.ConfigName);
        Assert.Equal(30.1, oval.YourBestLapSeconds, precision: 3);
        var road = result.Shared.TrackPace.Single(p => p.TrackId == 22);
        Assert.Equal("Road Course B", road.ConfigName);
        Assert.Equal(95.2, road.YourBestLapSeconds, precision: 3);
    }

    [Fact]
    public async Task CompareAsync_NoSharedRaces_EmptyHeadToHead_SidesStillPresent()
    {
        await using var db = DbContextFactory.Create();
        SeedRace(db, 1, 10, "Spa", DateTimeOffset.UtcNow);
        SeedRace(db, 2, 20, "Monza", DateTimeOffset.UtcNow);
        SeedResult(db, 1, Me, finish: 1, oldIr: 2000, newIr: 2010, incidents: 0, bestLap: 120.0);
        SeedResult(db, 2, Rival, finish: 1, oldIr: 1900, newIr: 1910, incidents: 0, bestLap: 121.0);
        await db.SaveChangesAsync(Ct);
        var service = Build(db);

        var result = await service.CompareAsync(Me, Rival, Ct);

        Assert.Equal(0, result.Shared.TotalShared);
        Assert.Empty(result.Shared.Races);
        Assert.Empty(result.Shared.TrackPace);
        Assert.Equal("You", result.You.DisplayName);
        Assert.Equal("Rival", result.Rival.DisplayName);
    }
}
