using System.Text.Json;
using ApexRacers.Ingestion;
using Aydsko.iRacingData.Common;
using Xunit;

namespace ApexRacers.Tests.Ingestion;

public class TrackStateIngestTests
{
    [Fact]
    public void ToSnapshot_MapsEverySdkFieldAndPinsThePersistedWireNames()
    {
        var snapshot = TrackStateIngest.ToSnapshot(new TrackState
        {
            LeaveMarbles = true,
            PracticeRubber = 1,
            QualifyRubber = 2,
            WarmupRubber = 3,
            RaceRubber = 4,
            PracticeGripCompound = 5,
            QualifyGripCompound = 6,
            WarmupGripCompound = 7,
            RaceGripCompound = 8,
        });

        Assert.NotNull(snapshot);
        Assert.True(snapshot.LeaveMarbles);
        Assert.Equal(1, snapshot.PracticeRubber);
        Assert.Equal(2, snapshot.QualifyRubber);
        Assert.Equal(3, snapshot.WarmupRubber);
        Assert.Equal(4, snapshot.RaceRubber);
        Assert.Equal(5, snapshot.PracticeGripCompound);
        Assert.Equal(6, snapshot.QualifyGripCompound);
        Assert.Equal(7, snapshot.WarmupGripCompound);
        Assert.Equal(8, snapshot.RaceGripCompound);

        var json = JsonSerializer.Serialize(snapshot);
        Assert.Contains("\"leave_marbles\":true", json);
        Assert.Contains("\"practice_rubber\":1", json);
        Assert.Contains("\"qualify_rubber\":2", json);
        Assert.Contains("\"warmup_rubber\":3", json);
        Assert.Contains("\"race_rubber\":4", json);
        Assert.Contains("\"practice_grip_compound\":5", json);
        Assert.Contains("\"qualify_grip_compound\":6", json);
        Assert.Contains("\"warmup_grip_compound\":7", json);
        Assert.Contains("\"race_grip_compound\":8", json);
    }

    [Fact]
    public void ToSnapshot_NullSource_ReturnsNull()
    {
        Assert.Null(TrackStateIngest.ToSnapshot(null));
    }
}
