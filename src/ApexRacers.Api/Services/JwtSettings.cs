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
/// that had to match. Now the signing key can only be derived one way.</para>
/// </summary>
public sealed record JwtSettings(string SigningKey, string Issuer, string Audience)
{
    public const string DefaultIssuer = "ApexRacers.Api";
    public const string DefaultAudience = "ApexRacers.Web";

    /// <summary>
    /// Reads the settings, failing fast when the signing key is absent. Issuer and audience have
    /// defaults because they only need to agree with each other; the signing key does not, because
    /// a missing one would otherwise surface as an unexplained 401 at the first request.
    /// </summary>
    public static JwtSettings FromConfiguration(IConfiguration config) => new(
        config["JWT_SIGNING_KEY"]
            ?? throw new InvalidOperationException("JWT_SIGNING_KEY is not set."),
        config["JWT_ISSUER"] ?? DefaultIssuer,
        config["JWT_AUDIENCE"] ?? DefaultAudience);

    /// <summary>The signing key, derived identically wherever it is needed.</summary>
    public SymmetricSecurityKey SecurityKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}
