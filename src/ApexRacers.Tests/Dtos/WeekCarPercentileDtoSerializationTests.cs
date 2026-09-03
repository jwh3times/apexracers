using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using Xunit;

namespace ApexRacers.Tests.Dtos;

public class WeekCarPercentileDtoSerializationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(LapEvidence.RaceLap)]
    [InlineData(LapEvidence.UploadedLap)]
    public void PersonalBestLapEvidence_UsesCanonicalPropertyName(LapEvidence evidence)
    {
        var dto = new WeekCarPercentileDto(1, 75, 25, evidence);

        var json = JsonSerializer.SerializeToElement(dto, WebJson);

        Assert.Equal(evidence.ToString(), json.GetProperty("personalBestLapEvidence").GetString());
        Assert.False(json.TryGetProperty("bestLapEvidence", out _));
    }
}
