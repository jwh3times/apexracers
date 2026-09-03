using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Models;

public class ConfigurationNameTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N/A")]
    [InlineData(" n/a ")]
    public void Normalize_AbsentUpstreamSpellings_UsesEmptyStorageValue(string? value)
    {
        Assert.Equal(string.Empty, ConfigurationName.Normalize(value));
    }

    [Fact]
    public void Normalize_RealConfiguration_PreservesItsLabel()
    {
        Assert.Equal("Grand Prix", ConfigurationName.Normalize("Grand Prix"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("N/A")]
    public void NullIfAbsent_AbsentSpellings_UseNullResponseValue(string? value)
    {
        Assert.Null(ConfigurationName.NullIfAbsent(value));
    }

    [Fact]
    public void NullIfAbsent_RealConfiguration_PreservesItsLabel()
    {
        Assert.Equal("Grand Prix", ConfigurationName.NullIfAbsent("Grand Prix"));
    }
}
