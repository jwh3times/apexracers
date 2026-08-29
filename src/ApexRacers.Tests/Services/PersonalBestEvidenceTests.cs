using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using Xunit;

namespace ApexRacers.Tests.Services;

public class PersonalBestEvidenceTests
{
    [Fact]
    public void OfficialRaceLapsOnly_ExcludesUploadedLaps()
    {
        var uploadedLap = new UploadedLap
        {
            SessionType = LapSessionType.Race,
        };

        var eligible = PersonalBestEvidence.OfficialRaceLapsOnly
            .ScopeUploadedLaps(new[] { uploadedLap }.AsQueryable())
            .ToList();

        Assert.Empty(eligible);
    }

    [Fact]
    public void FromRequest_FilteredUploadedLaps_FiltersTypesAndKeepsUnknown()
    {
        var race = new UploadedLap { SessionType = LapSessionType.Race };
        var practice = new UploadedLap { SessionType = LapSessionType.Practice };
        var unknown = new UploadedLap { SessionType = LapSessionType.Unknown };

        var evidence = PersonalBestEvidence.FromRequest(
            includeUploadedLaps: true,
            uploadedLapTypes: [LapSessionType.Race]);

        var eligible = evidence.ScopeUploadedLaps(
            new[] { race, practice, unknown }.AsQueryable());

        Assert.Equal([race, unknown], eligible);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FromRequest_NoTypeFilter_AllowsEveryUploadedType(bool emptyList)
    {
        var race = new UploadedLap { SessionType = LapSessionType.Race };
        var practice = new UploadedLap { SessionType = LapSessionType.Practice };
        IReadOnlyList<LapSessionType>? types = emptyList ? [] : null;

        var evidence = PersonalBestEvidence.FromRequest(true, types);

        Assert.Equal([race, practice], evidence.ScopeUploadedLaps(new[] { race, practice }.AsQueryable()));
    }
}
