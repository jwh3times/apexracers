using ApexRacers.Api.Services;
using Xunit;

namespace ApexRacers.Tests.Services;

public class SharedRaceAnalysisTests
{
    private static SharedRaceInput Race(
        int id, int month, string track, int yourFinish, int rivalFinish,
        double yourLap = -1, double rivalLap = -1,
        int trackId = 1, string configName = "") =>
        new(id, new DateTimeOffset(2026, month, 1, 0, 0, 0, TimeSpan.Zero), trackId, track, configName,
            yourFinish, rivalFinish, 10, -10, 1, 2, yourLap, rivalLap);

    [Fact]
    public void Summarize_EmptyList_AllZeroAndEmpty()
    {
        var result = SharedRaceAnalysis.Summarize([]);

        Assert.Equal(0, result.TotalShared);
        Assert.Equal(0, result.YouAhead);
        Assert.Equal(0, result.RivalAhead);
        Assert.Empty(result.Races);
        Assert.Empty(result.TrackPace);
    }

    [Fact]
    public void Summarize_CountsWhoFinishedAhead_LowerPositionWins()
    {
        var result = SharedRaceAnalysis.Summarize([
            Race(1, 1, "Spa", yourFinish: 3, rivalFinish: 5),   // you ahead
            Race(2, 2, "Spa", yourFinish: 8, rivalFinish: 2),   // rival ahead
            Race(3, 3, "Monza", yourFinish: 1, rivalFinish: 4, trackId: 2), // you ahead
        ]);

        Assert.Equal(3, result.TotalShared);
        Assert.Equal(2, result.YouAhead);
        Assert.Equal(1, result.RivalAhead);
    }

    [Fact]
    public void Summarize_EqualFinish_CountsNeitherAhead()
    {
        // Defensive: two drivers can't truly tie an overall finish, but never miscount if they do.
        var result = SharedRaceAnalysis.Summarize([Race(1, 1, "Spa", 4, 4)]);

        Assert.Equal(1, result.TotalShared);
        Assert.Equal(0, result.YouAhead);
        Assert.Equal(0, result.RivalAhead);
    }

    [Fact]
    public void Summarize_OrdersRacesNewestFirst()
    {
        var result = SharedRaceAnalysis.Summarize([
            Race(1, 1, "Spa", 3, 5),
            Race(3, 3, "Monza", 1, 4, trackId: 2),
            Race(2, 2, "Spa", 8, 2),
        ]);

        Assert.Equal([3, 2, 1], result.Races.Select(r => r.SubsessionId).ToArray());
    }

    [Fact]
    public void Summarize_BestLapPerTrack_TakesFastestValidLapPerSide()
    {
        var result = SharedRaceAnalysis.Summarize([
            Race(1, 1, "Spa", 3, 5, yourLap: 120.5, rivalLap: 121.0),
            Race(2, 2, "Spa", 4, 6, yourLap: 119.8, rivalLap: 122.0), // your faster Spa lap
            Race(3, 3, "Monza", 1, 2, yourLap: 105.2, rivalLap: 104.9, trackId: 2),
        ]);

        var spa = result.TrackPace.Single(p => p.TrackId == 1);
        Assert.Equal("Spa", spa.TrackName);
        Assert.Equal(119.8, spa.YourBestLapSeconds, precision: 3);
        Assert.Equal(121.0, spa.RivalBestLapSeconds, precision: 3);

        var monza = result.TrackPace.Single(p => p.TrackId == 2);
        Assert.Equal(105.2, monza.YourBestLapSeconds, precision: 3);
        Assert.Equal(104.9, monza.RivalBestLapSeconds, precision: 3);
    }

    [Fact]
    public void Summarize_BestLapPerTrack_IgnoresSentinelLaps_AndReturnsMinusOneWhenNoneValid()
    {
        var result = SharedRaceAnalysis.Summarize([
            Race(1, 1, "Spa", 3, 5, yourLap: -1, rivalLap: 0),     // both sentinel
            Race(2, 2, "Spa", 4, 6, yourLap: 119.8, rivalLap: -1), // only you valid
        ]);

        var spa = result.TrackPace.Single(p => p.TrackId == 1);
        Assert.Equal(119.8, spa.YourBestLapSeconds, precision: 3);
        Assert.Equal(-1, spa.RivalBestLapSeconds, precision: 3);
    }

    [Fact]
    public void Summarize_BestLapPerTrack_KeepsLayoutsAtOneVenueApart()
    {
        // Homestead's oval and road course share a Track Name; a 1.50 mi lap must never be
        // ranked against a 2.30 mi one.
        var result = SharedRaceAnalysis.Summarize([
            Race(1, 1, "Homestead Miami Speedway", 3, 5, yourLap: 30.1, rivalLap: 30.4,
                trackId: 20, configName: "Oval"),
            Race(2, 2, "Homestead Miami Speedway", 4, 6, yourLap: 95.2, rivalLap: 94.8,
                trackId: 22, configName: "Road Course B"),
        ]);

        Assert.Equal(2, result.TrackPace.Count);

        var oval = result.TrackPace.Single(p => p.TrackId == 20);
        Assert.Equal("Oval", oval.ConfigName);
        Assert.Equal(30.1, oval.YourBestLapSeconds, precision: 3);
        Assert.Equal(30.4, oval.RivalBestLapSeconds, precision: 3);

        var road = result.TrackPace.Single(p => p.TrackId == 22);
        Assert.Equal("Road Course B", road.ConfigName);
        Assert.Equal(95.2, road.YourBestLapSeconds, precision: 3);
        Assert.Equal(94.8, road.RivalBestLapSeconds, precision: 3);
    }

    [Fact]
    public void Summarize_BestLapPerTrack_GroupsOneTrackDespiteNameWhitespaceDrift()
    {
        // Eight track identifiers carry a trailing space in their name; grouping on identity
        // means the drift can't split one Track into two rows.
        var result = SharedRaceAnalysis.Summarize([
            Race(1, 1, "Charlotte Motor Speedway", 3, 5, yourLap: 50.4, rivalLap: 51.0, trackId: 9),
            Race(2, 2, "Charlotte Motor Speedway ", 4, 6, yourLap: 49.9, rivalLap: 50.2, trackId: 9),
        ]);

        var row = Assert.Single(result.TrackPace);
        Assert.Equal(9, row.TrackId);
        Assert.Equal(49.9, row.YourBestLapSeconds, precision: 3);
        Assert.Equal(50.2, row.RivalBestLapSeconds, precision: 3);
    }

    [Fact]
    public void Summarize_BestLapPerTrack_ReportsAbsentConfigurationAsNull()
    {
        var result = SharedRaceAnalysis.Summarize([Race(1, 1, "Spa", 3, 5, configName: "  ")]);

        Assert.Null(Assert.Single(result.TrackPace).ConfigName);
    }

    [Fact]
    public void Summarize_BestLapPerTrack_OrdersByNameThenConfiguration()
    {
        var result = SharedRaceAnalysis.Summarize([
            Race(1, 1, "Spa", 3, 5, trackId: 3),
            Race(2, 2, "Homestead Miami Speedway", 3, 5, trackId: 22, configName: "Road Course B"),
            Race(3, 3, "Homestead Miami Speedway", 3, 5, trackId: 20, configName: "Oval"),
        ]);

        Assert.Equal([20, 22, 3], result.TrackPace.Select(p => p.TrackId).ToArray());
    }

    [Fact]
    public void Summarize_MapsRowFieldsThrough()
    {
        var result = SharedRaceAnalysis.Summarize([Race(7, 5, "Spa", 3, 5)]);

        var row = Assert.Single(result.Races);
        Assert.Equal(7, row.SubsessionId);
        Assert.Equal("Spa", row.TrackName);
        Assert.Equal(3, row.YourFinish);
        Assert.Equal(5, row.RivalFinish);
        Assert.Equal(10, row.YourIRatingDelta);
        Assert.Equal(-10, row.RivalIRatingDelta);
        Assert.Equal(1, row.YourIncidents);
        Assert.Equal(2, row.RivalIncidents);
    }
}
