using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ApexRacers.Api.Services;

/// <summary>
/// The bearer-token contract, bound once and shared by both sides of it.
///
/// <para><b>Why this exists.</b> The issuing side (<see cref="AuthService.GenerateJwtAsync"/>) and
/// the validating side (<c>Program.cs</c>'s <c>TokenValidationParameters</c>, with
/// <c>ValidateIssuer</c> and <c>ValidateAudience</c> both on) each read the same two settings and
/// each carried their own copy of the fallback literals. Changing one default would have made
/// every token this API mints be rejected by this same API — a total-auth outage from a one-word
/// edit, with no compile error to catch it and no test to fail, since the test suite sets neither
/// key and so exercised both sides on the defaults.</para>
///
/// <para><see cref="SecurityKey"/> exists for the same reason: both sides previously did their own
/// <c>new SymmetricSecurityKey(Encoding.UTF8.GetBytes(...))</c>, so the encoding was a third thing
/// that had to match. <see cref="IssuingCredentials"/> and <see cref="ValidationParameters"/>
/// extend that to the whole of each side's construction, so <see cref="Algorithm"/> cannot be
/// pinned on one side and left open on the other.</para>
/// </summary>
public sealed record JwtSettings(string SigningKey, string Issuer, string Audience)
{
    public const string DefaultIssuer = "ApexRacers.Api";
    public const string DefaultAudience = "ApexRacers.Web";

    /// <summary>
    /// The only algorithm this API issues with or accepts. Pinning it on the validating side is
    /// defence in depth — every algorithm the library would otherwise accept for a symmetric key
    /// still needs that key — but it costs nothing and keeps the accepted set from widening as the
    /// library's defaults change.
    /// </summary>
    public const string Algorithm = SecurityAlgorithms.HmacSha256;

    /// <summary>
    /// Minimum signing-key length, in UTF-8 bytes. HMAC-SHA256's security is bounded by the key,
    /// so a key shorter than the 256-bit digest is the weakest link and is brute-forceable offline
    /// from any single captured token. Measured in bytes rather than characters because
    /// <see cref="SecurityKey"/> feeds the UTF-8 encoding of the string to HMAC — a 32-character
    /// key of multi-byte characters is fine, a 20-character one is not.
    /// </summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>
    /// Reads the settings, failing fast when the signing key is absent or too short. Issuer and
    /// audience have defaults because they only need to agree with each other; the signing key
    /// does not, because a missing one would otherwise surface as an unexplained 401 at the first
    /// request. Length is checked here, at the single bind point, rather than at either use site —
    /// a weak key that boots is a weak key in production, and startup is the last moment an
    /// operator sees the reason.
    /// </summary>
    public static JwtSettings FromConfiguration(IConfiguration config)
    {
        var signingKey = config["JWT_SIGNING_KEY"];
        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException("JWT_SIGNING_KEY is not set.");

        var length = Encoding.UTF8.GetByteCount(signingKey);
        if (length < MinimumSigningKeyBytes)
            throw new InvalidOperationException(
                $"JWT_SIGNING_KEY must be at least {MinimumSigningKeyBytes} bytes " +
                $"({MinimumSigningKeyBytes * 8} bits) to match {Algorithm}; the configured value " +
                $"is {length}. Generate one with: openssl rand -base64 48");

        return new JwtSettings(
            signingKey,
            config["JWT_ISSUER"] ?? DefaultIssuer,
            config["JWT_AUDIENCE"] ?? DefaultAudience);
    }

    /// <summary>The signing key, derived identically wherever it is needed.</summary>
    public SymmetricSecurityKey SecurityKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    /// <summary>The credentials the issuing side signs with.</summary>
    public SigningCredentials IssuingCredentials() => new(SecurityKey(), Algorithm);

    /// <summary>
    /// The parameters the validating side checks against — the mirror image of
    /// <see cref="IssuingCredentials"/>, built from the same settings so the two cannot disagree.
    /// </summary>
    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SecurityKey(),
        ValidAlgorithms = [Algorithm],
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ClockSkew = TimeSpan.Zero,
    };
}
