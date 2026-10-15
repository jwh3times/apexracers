using ApexRacers.Api.Services.PercentileHistory;
using ApexRacers.Core.Models;
using Xunit;

namespace ApexRacers.Tests.Services;

public sealed class PercentileSelectionTests
{
    [Fact]
    public void UploadsOffIgnoresEvenUnreviewedArguments()
    {
        var off = PercentileSelection.FromRequest(false, null);
        Assert.Equal(off, PercentileSelection.FromRequest(false, [(LapSessionType)255]));
        Assert.False(off.Includes(LapSessionType.Unknown));
        Assert.Equal("v1:0:0", off.Key);
    }

    [Fact]
    public void NullAndEmptyUnrestrictedAreEqualButExplicitAllRemainsDistinct()
    {
        var unrestricted = PercentileSelection.FromRequest(true, null);
        Assert.Equal(unrestricted, PercentileSelection.FromRequest(true, []));
        var explicitAll = PercentileSelection.FromRequest(true, [LapSessionType.Unknown, LapSessionType.Practice,
            LapSessionType.Qualifying, LapSessionType.TimeTrial, LapSessionType.Race, LapSessionType.LoneQualify]);
        Assert.NotEqual(unrestricted, explicitAll);
        Assert.NotEqual(unrestricted.Key, explicitAll.Key);
        Assert.Equal("v1:1:63", unrestricted.Key);
        Assert.Equal("v1:2:63", explicitAll.Key);
    }

    [Fact]
    public void RestrictedTypesAreAnOrderIndependentSetWithAutomaticUnknown()
    {
        var selection = PercentileSelection.FromRequest(true, [LapSessionType.Race, LapSessionType.Practice, LapSessionType.Race]);
        Assert.Equal(selection, PercentileSelection.FromRequest(true, [LapSessionType.Unknown, LapSessionType.Practice, LapSessionType.Race]));
        Assert.True(selection.Includes(LapSessionType.Unknown));
        Assert.True(selection.Includes(LapSessionType.Race));
        Assert.False(selection.Includes(LapSessionType.Qualifying));
        Assert.Equal("v1:2:19", selection.Key);
    }

    [Fact]
    public void UnknownOnlyIsNotUnrestricted()
    {
        var unknown = PercentileSelection.FromRequest(true, [LapSessionType.Unknown]);
        Assert.NotEqual(unknown, PercentileSelection.FromRequest(true, null));
        Assert.True(unknown.Includes(LapSessionType.Unknown));
        Assert.False(unknown.Includes(LapSessionType.Practice));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(255)]
    public void VersionOneDoesNotSilentlyAdmitFutureNumericTypes(byte value)
    {
        var type = (LapSessionType)value;
        Assert.Throws<ArgumentException>(() => PercentileSelection.FromRequest(true, [type]));
        Assert.False(PercentileSelection.FromRequest(true, null).Includes(type));
    }

    [Fact]
    public void CanonicalValueDoesNotRetainMutableRequestList()
    {
        var types = new List<LapSessionType> { LapSessionType.Practice };
        var selection = PercentileSelection.FromRequest(true, types);
        types.Clear(); types.Add(LapSessionType.Race);
        Assert.True(selection.Includes(LapSessionType.Practice));
        Assert.False(selection.Includes(LapSessionType.Race));
        Assert.Equal(1, selection.PolicyVersion);
    }
}
