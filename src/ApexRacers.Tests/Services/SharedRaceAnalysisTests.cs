using ApexRacers.Api.Services;
using Xunit;

namespace ApexRacers.Tests.Services;

public class SharedRaceAnalysisTests
{
    private static SharedRaceInput Race(
        int id, int month, string track, int yourFinish, int rivalFinish,
        double yourLap = -1, double rivalLap = -1) =>
        new(id, new DateTimeOffset(2026, month, 1, 0, 0, 0, TimeSpan.Zero), track,
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
            Race(3, 3, "Monza", yourFinish: 1, rivalFinish: 4), // you ahead
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
            Race(3, 3, "Monza", 1, 4),
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
            Race(3, 3, "Monza", 1, 2, yourLap: 105.2, rivalLap: 104.9),
        ]);

        var spa = result.TrackPace.Single(p => p.TrackName == "Spa");
        Assert.Equal(119.8, spa.YourBestLapSeconds, precision: 3);
        Assert.Equal(121.0, spa.RivalBestLapSeconds, precision: 3);

        var monza = result.TrackPace.Single(p => p.TrackName == "Monza");
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

        var spa = result.TrackPace.Single(p => p.TrackName == "Spa");
        Assert.Equal(119.8, spa.YourBestLapSeconds, precision: 3);
        Assert.Equal(-1, spa.RivalBestLapSeconds, precision: 3);
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
