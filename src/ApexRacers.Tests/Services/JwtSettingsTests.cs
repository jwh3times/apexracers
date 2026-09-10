using System.IdentityModel.Tokens.Jwt;
using System.Text;
using ApexRacers.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
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
    public void ABlankSigningKeyIsTreatedAsMissing()
    {
        // A variable that is set but empty is an operator mistake, not a configured key, and it
        // would otherwise be a zero-byte HMAC key.
        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtSettings.FromConfiguration(Config(("JWT_SIGNING_KEY", "   "))));

        Assert.Contains("JWT_SIGNING_KEY", ex.Message);
    }

    [Fact]
    public void ASigningKeyShorterThanTheMinimumFailsFast()
    {
        // 31 bytes — one short. A key weaker than the HMAC-SHA256 digest is brute-forceable
        // offline from a single captured token, so this must not be allowed to boot.
        var shortKey = new string('k', JwtSettings.MinimumSigningKeyBytes - 1);

        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtSettings.FromConfiguration(Config(("JWT_SIGNING_KEY", shortKey))));

        Assert.Contains("JWT_SIGNING_KEY", ex.Message);
        Assert.Contains(JwtSettings.MinimumSigningKeyBytes.ToString(), ex.Message);
        // The operator has to be told the actual length, or "too short" is unactionable.
        Assert.Contains((JwtSettings.MinimumSigningKeyBytes - 1).ToString(), ex.Message);
    }

    [Fact]
    public void ASigningKeyOfExactlyTheMinimumIsAccepted()
    {
        var exactKey = new string('k', JwtSettings.MinimumSigningKeyBytes);

        var settings = JwtSettings.FromConfiguration(Config(("JWT_SIGNING_KEY", exactKey)));

        Assert.Equal(exactKey, settings.SigningKey);
    }

    [Fact]
    public void TheMinimumIsMeasuredInBytesNotCharacters()
    {
        // The key reaches HMAC as its UTF-8 encoding, so bytes are what count. Twelve three-byte
        // characters is 36 bytes — comfortably above the minimum, and well under it by character
        // count. Measuring characters would reject this key for no reason.
        var multiByteKey = new string('セ', 12);
        Assert.True(multiByteKey.Length < JwtSettings.MinimumSigningKeyBytes);
        Assert.True(Encoding.UTF8.GetByteCount(multiByteKey) >= JwtSettings.MinimumSigningKeyBytes);

        var settings = JwtSettings.FromConfiguration(Config(("JWT_SIGNING_KEY", multiByteKey)));

        Assert.Equal(multiByteKey, settings.SigningKey);
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

    [Fact]
    public void BothSidesAreBuiltOnTheOnePinnedAlgorithm()
    {
        var settings = new JwtSettings(Key, "i", "a");

        Assert.Equal(SecurityAlgorithms.HmacSha256, JwtSettings.Algorithm);
        Assert.Equal(JwtSettings.Algorithm, settings.IssuingCredentials().Algorithm);
        Assert.Equal([JwtSettings.Algorithm], settings.ValidationParameters().ValidAlgorithms);
    }

    [Fact]
    public void ValidationParametersMirrorTheSettings()
    {
        var settings = new JwtSettings(Key, "some-issuer", "some-audience");

        var parameters = settings.ValidationParameters();

        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.Equal("some-issuer", parameters.ValidIssuer);
        Assert.Equal("some-audience", parameters.ValidAudience);
        Assert.Equal(TimeSpan.Zero, parameters.ClockSkew);
        Assert.Equal(
            settings.SecurityKey().Key,
            Assert.IsType<SymmetricSecurityKey>(parameters.IssuerSigningKey).Key);
    }

    [Fact]
    public void ATokenSignedWithAnotherHmacAlgorithmIsRejected()
    {
        // The point of pinning: without ValidAlgorithms, any HMAC variant the library supports
        // validates against the same symmetric key. HS512 needs a 64-byte key of its own, so this
        // uses one long enough to sign with and validates it under settings that pin HS256.
        var longKey = new string('k', 64);
        var settings = new JwtSettings(longKey, "some-issuer", "some-audience");
        var hs512 = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                settings.SecurityKey(), SecurityAlgorithms.HmacSha512));
        var token = new JwtSecurityTokenHandler().WriteToken(hs512);

        // The library reports the algorithm mismatch as a failure to find a usable signing key,
        // so match on the family rather than the exact type.
        Assert.ThrowsAny<SecurityTokenValidationException>(
            () => new JwtSecurityTokenHandler()
                .ValidateToken(token, settings.ValidationParameters(), out _));

        // ...and the same token validates once only the pin is removed, which is the state this
        // API shipped in before. That is what makes the assertion above non-vacuous: the pin, and
        // nothing else about these parameters, is what rejects it.
        var unpinned = settings.ValidationParameters();
        unpinned.ValidAlgorithms = null;
        Assert.NotNull(new JwtSecurityTokenHandler().ValidateToken(token, unpinned, out _));
    }
}
