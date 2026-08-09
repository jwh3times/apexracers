using System.Text;
using ApexRacers.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ApexRacers.Tests.Services;

public class JwtSettingsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    private const string Key = "unit-test-signing-key-minimum-32-bytes-long!";

    [Fact]
    public void UsesTheDocumentedDefaultsWhenIssuerAndAudienceAreUnset()
    {
        var settings = JwtSettings.FromConfiguration(Config(("JWT_SIGNING_KEY", Key)));

        Assert.Equal("ApexRacers.Api", settings.Issuer);
        Assert.Equal("ApexRacers.Web", settings.Audience);
        Assert.Equal(JwtSettings.DefaultIssuer, settings.Issuer);
        Assert.Equal(JwtSettings.DefaultAudience, settings.Audience);
    }

    [Fact]
    public void ConfigurationOverridesTheDefaults()
    {
        var settings = JwtSettings.FromConfiguration(Config(
            ("JWT_SIGNING_KEY", Key),
            ("JWT_ISSUER", "custom-issuer"),
            ("JWT_AUDIENCE", "custom-audience")));

        Assert.Equal("custom-issuer", settings.Issuer);
        Assert.Equal("custom-audience", settings.Audience);
    }

    [Fact]
    public void MissingSigningKeyFailsFast()
    {
        // Unlike issuer/audience, this has no sensible default — a missing key would otherwise
        // surface as an unexplained 401 on the first authenticated request.
        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtSettings.FromConfiguration(Config()));

        Assert.Contains("JWT_SIGNING_KEY", ex.Message);
    }

    [Fact]
    public void SecurityKeyDerivesFromTheSigningKeyBytes()
    {
        var settings = new JwtSettings(Key, "i", "a");

        Assert.Equal(Encoding.UTF8.GetBytes(Key), settings.SecurityKey().Key);
    }

    [Fact]
    public void SecurityKeyIsStableAcrossCalls()
    {
        // The issuing and validating sides each call this independently; if it were not
        // deterministic they would disagree about the signature.
        var settings = new JwtSettings(Key, "i", "a");

        Assert.Equal(settings.SecurityKey().Key, settings.SecurityKey().Key);
    }
}
