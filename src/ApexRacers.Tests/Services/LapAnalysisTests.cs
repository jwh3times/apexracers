using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using System.Text.Json;
using Xunit;

namespace ApexRacers.Tests.Services;

public class LapAnalysisTests
{
    private static LapDto Lap(int n, double seconds, bool incident = false) =>
        new(n, seconds, incident, seconds > 0);

    [Fact]
    public void LapDto_SerializesTimedLapDomainLanguage()
    {
        var dto = Lap(1, 60.0);

        var json = JsonSerializer.SerializeToElement(dto, JsonSerializerOptions.Web);
        var propertyNames = json.EnumerateObject().Select(property => property.Name).ToList();

        Assert.Contains("timed", propertyNames);
        Assert.DoesNotContain("valid", propertyNames);
        Assert.True(json.GetProperty("timed").GetBoolean());
    }

    [Fact]
    public void Compute_CleanLaps_MeanFastestAndPositiveDeg()
    {
        var (mean, std, fastest, deg) = LapAnalysis.Compute(
            [Lap(1, 60.0), Lap(2, 60.5), Lap(3, 61.0)]);

        Assert.Equal(60.5, mean, precision: 3);
        Assert.Equal(0.408, std, precision: 3); // population std of {60,60.5,61}
        Assert.Equal(60.0, fastest, precision: 3);
        Assert.Equal(0.5, deg, precision: 3); // +0.5s per lap
    }

    [Fact]
    public void Compute_ExcludesIncidentLapsFromMeanAndDeg_ButNotFastest()
    {
        // Lap 2 is a slow incident lap — ignored by mean/std/deg, still eligible as fastest source.
        var (mean, _, fastest, deg) = LapAnalysis.Compute(
            [Lap(1, 60.0), Lap(2, 90.0, incident: true), Lap(3, 61.0)]);

        Assert.Equal(60.5, mean, precision: 3); // (60 + 61) / 2
        Assert.Equal(60.0, fastest, precision: 3);
        Assert.Equal(0.5, deg, precision: 3); // slope over (1,60),(3,61)
    }

    [Fact]
    public void Compute_IgnoresUntimedLaps()
    {
        var (mean, _, fastest, _) = LapAnalysis.Compute([Lap(1, -1), Lap(2, 60.0)]);

        Assert.Equal(60.0, mean, precision: 3);
        Assert.Equal(60.0, fastest, precision: 3);
    }

    [Fact]
    public void Compute_SingleGreenLap_ZeroDegAndStd()
    {
        var (mean, std, fastest, deg) = LapAnalysis.Compute([Lap(1, 60.0)]);

        Assert.Equal(60.0, mean, precision: 3);
        Assert.Equal(0d, std);
        Assert.Equal(60.0, fastest, precision: 3);
        Assert.Equal(0d, deg);
    }

    [Fact]
    public void Compute_NoTimedLaps_AllZero()
    {
        var (mean, std, fastest, deg) = LapAnalysis.Compute([Lap(1, -1), Lap(2, 0)]);

        Assert.Equal(0d, mean);
        Assert.Equal(0d, std);
        Assert.Equal(0d, fastest);
        Assert.Equal(0d, deg);
    }

    [Fact]
    public void Compute_AllIncidentLaps_FastestSetButGreenStatsZero()
    {
        var (mean, std, fastest, deg) = LapAnalysis.Compute(
            [Lap(1, 60.0, incident: true), Lap(2, 61.0, incident: true)]);

        Assert.Equal(0d, mean); // no green laps
        Assert.Equal(0d, std);
        Assert.Equal(60.0, fastest, precision: 3); // still the quickest timed lap
        Assert.Equal(0d, deg);
    }
}
