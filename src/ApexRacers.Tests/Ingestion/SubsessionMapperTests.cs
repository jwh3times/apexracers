using ApexRacers.Ingestion;
using Aydsko.iRacingData.Common;
using Aydsko.iRacingData.Results;
using Xunit;

namespace ApexRacers.Tests.Ingestion;

public class SubsessionMapperTests
{
    [Fact]
    public void ToTrackEntity_ResultsNotApplicableConfiguration_StoresAbsence()
    {
        var source = new SubSessionResult
        {
            Track = new Track { TrackId = 18, TrackName = "Richmond Raceway", ConfigName = "N/A" },
        };

        var entity = SubsessionMapper.ToTrackEntity(source);

        Assert.Equal(18, entity.Id);
        Assert.Equal("Richmond Raceway", entity.Name);
        Assert.Equal(string.Empty, entity.ConfigName);
    }

    [Fact]
    public void ToEntity_MapsEveryPersistedSubsessionField()
    {
        var weekId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 8, 9, 14, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(45);
        var source = new SubSessionResult
        {
            SessionId = 311244815,
            SeasonId = 501,
            RaceWeekIndex = 7,
            Track = new Track { TrackId = 18, TrackName = "Spa" },
            OfficialSession = true,
            EventStrengthOfField = 2875,
            StartTime = start,
            EndTime = end,
            NumberOfCautions = 3,
            NumberOfCautionLaps = 8,
            NumberOfLeadChanges = 11,
            CornersPerLap = 20,
            EventAverageLap = TimeSpan.FromSeconds(131.25),
            EventBestLapTime = TimeSpan.FromSeconds(129.75),
            EventLapsComplete = 42,
            Weather = new ResultsWeather
            {
                TemperatureValue = 24,
                TemperatureUnits = 1,
                RelativeHumidity = 55,
            },
            TrackState = new TrackState
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
            },
        };

        var entity = SubsessionMapper.ToEntity(
            99001, source, weekId, new SubsessionIndexer.SplitPosition(2, 5),
            new SubsessionIndexer.EntryTally(Classified: 18, AiEntries: 1, TeamEntries: 4));

        Assert.Equal(99001, entity.Id);
        Assert.Equal(311244815, entity.RaceSessionId);
        Assert.Equal(501, entity.SeasonId);
        Assert.Equal(7, entity.RaceWeekIndex);
        Assert.Equal(weekId, entity.WeekId);
        Assert.Equal(18, entity.TrackId);
        Assert.True(entity.OfficialSession);
        Assert.Equal(2875, entity.EventStrengthOfField);
        Assert.Equal(start, entity.StartTime);
        Assert.Equal(end, entity.EndTime);
        Assert.Equal(2, entity.SplitIndex);
        Assert.Equal(5, entity.SplitCount);
        Assert.Equal(4, entity.TeamEntryCount);
        Assert.Equal(1, entity.AiEntryCount);
        Assert.Equal(3, entity.NumCautions);
        Assert.Equal(8, entity.NumCautionLaps);
        Assert.Equal(11, entity.NumLeadChanges);
        Assert.Equal(20, entity.CornersPerLap);
        Assert.Equal(131.25, entity.EventAverageLapSeconds);
        Assert.Equal(129.75, entity.EventBestLapSeconds);
        Assert.Equal(42, entity.EventLapsComplete);
        Assert.Contains("\"temp_value\":24", entity.WeatherJson);
        Assert.Contains("\"leave_marbles\":true", entity.TrackStateJson);
        Assert.Contains("\"race_grip_compound\":8", entity.TrackStateJson);
    }

    [Fact]
    public void ToEntity_NullWireBlocksAndMissingEventLaps_MapToPersistedSentinels()
    {
        var source = new SubSessionResult
        {
            Track = new Track(),
            EventAverageLap = TimeSpan.Zero,
            EventBestLapTime = TimeSpan.Zero,
        };

        var entity = SubsessionMapper.ToEntity(
            1, source, weekId: null, splitPosition: null, entryTally: default);

        Assert.Equal(-1, entity.EventAverageLapSeconds);
        Assert.Equal(-1, entity.EventBestLapSeconds);
        Assert.Null(entity.WeatherJson);
        Assert.Null(entity.TrackStateJson);
        Assert.Null(entity.SplitIndex);
        Assert.Null(entity.SplitCount);
        Assert.Equal(0, entity.TeamEntryCount);
        Assert.Equal(0, entity.AiEntryCount);
    }

    [Fact]
    public void ToResult_MapsEveryPersistedResultField()
    {
        var source = new Result
        {
            CustomerId = 123456,
            CarId = 132,
            CarClassId = 33,
            FinishPosition = 4,
            FinishPositionInClass = 2,
            StartingPosition = 9,
            StartingPositionInClass = 5,
            Incidents = 7,
            BestLapTime = TimeSpan.FromSeconds(101.125),
            AverageLap = TimeSpan.FromSeconds(103.5),
            LapsComplete = 28,
            LapsLead = 6,
            ChampPoints = 84,
            AggregateChampionshipPoints = 211,
            NewIRating = 3021,
            OldIRating = 2978,
            NewCornersPerIncident = 47.25m,
            OldCornersPerIncident = 44.5m,
            ReasonOut = "Running",
            ReasonOutId = 0,
            Division = 3,
            DropRace = true,
            ClassInterval = TimeSpan.FromSeconds(12.75),
            DisplayName = "Ada Racer",
            QualifyingLapTime = TimeSpan.FromSeconds(100.25),
            NewSubLevel = 365,
            OldSubLevel = 342,
            NewTimeTrialRating = 1512,
            OldTimeTrialRating = 1490,
        };

        var entity = SubsessionMapper.ToResult(99001, source);

        Assert.Equal(99001, entity.SubsessionId);
        Assert.Equal(123456, entity.CustId);
        Assert.Equal(132, entity.CarId);
        Assert.Equal(33, entity.CarClassId);
        Assert.Equal(4, entity.FinishPosition);
        Assert.Equal(2, entity.FinishPositionInClass);
        Assert.Equal(9, entity.StartingPosition);
        Assert.Equal(5, entity.StartingPositionInClass);
        Assert.Equal(7, entity.Incidents);
        Assert.Equal(101.125, entity.BestLapSeconds);
        Assert.Equal(103.5, entity.AverageLapSeconds);
        Assert.Equal(28, entity.LapsComplete);
        Assert.Equal(6, entity.LapsLead);
        Assert.Equal(84, entity.ChampPoints);
        Assert.Equal(211, entity.AggregateChampPoints);
        Assert.Equal(3021, entity.NewIRating);
        Assert.Equal(2978, entity.OldIRating);
        Assert.Equal(47.25, entity.NewCpi);
        Assert.Equal(44.5, entity.OldCpi);
        Assert.Equal("Running", entity.ReasonOut);
        Assert.Equal(0, entity.ReasonOutId);
        Assert.Equal(3, entity.Division);
        Assert.True(entity.DropRace);
        Assert.Equal(12.75, entity.Interval);
        Assert.Equal("Ada Racer", entity.DisplayName);
        Assert.Equal(100.25, entity.QualLapSeconds);
        Assert.Equal(365, entity.NewSubLevel);
        Assert.Equal(342, entity.OldSubLevel);
        Assert.Equal(1512, entity.NewTtRating);
        Assert.Equal(1490, entity.OldTtRating);
    }

    [Fact]
    public void ToResult_NullOptionalRaceValues_MapToPersistedFallbacks()
    {
        var source = new Result { CustomerId = 1 };

        var entity = SubsessionMapper.ToResult(2, source);

        Assert.Equal(0, entity.StartingPositionInClass);
        Assert.Equal(-1, entity.BestLapSeconds);
        Assert.Equal(-1, entity.AverageLapSeconds);
        Assert.Equal(-1, entity.Interval);
        Assert.Equal(-1, entity.QualLapSeconds);
    }
}
