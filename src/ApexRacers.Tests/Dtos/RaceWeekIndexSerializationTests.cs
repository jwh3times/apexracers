using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using Xunit;

namespace ApexRacers.Tests.Dtos;

public class RaceWeekIndexSerializationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SeriesDto_UsesCurrentRaceWeekIndex()
    {
        var json = Serialize(new SeriesDto(
            1, "Series", 2, 3, "sports_car", "Track", "Layout", 4, 5));

        Assert.Equal(3, json.GetProperty("currentRaceWeekIndex").GetInt32());
        Assert.False(json.TryGetProperty("currentWeekNumber", out _));
    }

    [Fact]
    public void PercentileResultDto_UsesRaceWeekIndex()
    {
        var json = Serialize(new PercentileResultDto(
            SeriesId: 1,
            RaceWeekIndex: 2,
            CarId: 3,
            CustomerId: 4,
            PercentileRank: 50,
            FieldPosition: 3,
            TopSharePercent: 50,
            SampleSize: 5,
            IsPercentilePresentable: true,
            ComputedAt: DateTimeOffset.UnixEpoch,
            SeriesName: "Series",
            TrackName: "Track",
            TrackConfigName: "Layout",
            YourBestLapSeconds: 60,
            YourBestLapEvidence: LapEvidence.RaceLap,
            FieldBestLapSeconds: 59,
            FieldMedianLapSeconds: 61,
            Distribution: []));

        Assert.Equal(2, json.GetProperty("raceWeekIndex").GetInt32());
        Assert.False(json.TryGetProperty("weekNumber", out _));
    }

    [Fact]
    public void SeasonQualifyResultsDto_UsesCanonicalIndexNamesAtBothLevels()
    {
        var result = new SeasonQualifyResultDto(1, 2, "Driver", 3, 2000, 60, 4);
        var json = Serialize(new SeasonQualifyResultsDto(
            1, "Series", 2, "Class", [], 4, [0, 1, 2, 3, 4], [result]));

        Assert.Equal(4, json.GetProperty("raceWeekIndex").GetInt32());
        Assert.Equal(5, json.GetProperty("availableRaceWeekIndices").GetArrayLength());
        Assert.False(json.TryGetProperty("raceWeekNum", out _));
        Assert.False(json.TryGetProperty("availableWeeks", out _));

        var row = json.GetProperty("results")[0];
        Assert.Equal(4, row.GetProperty("raceWeekIndex").GetInt32());
        Assert.False(row.TryGetProperty("week", out _));
    }

    [Fact]
    public void RaceGuideEntryDto_UsesRaceWeekIndex()
    {
        var json = Serialize(new RaceGuideEntryDto(
            1, "Series", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), 20, 3));

        Assert.Equal(3, json.GetProperty("raceWeekIndex").GetInt32());
        Assert.False(json.TryGetProperty("raceWeekNum", out _));
    }

    private static JsonElement Serialize<T>(T value) =>
        JsonSerializer.SerializeToElement(value, WebJson);
}
