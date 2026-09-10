using System.IdentityModel.Tokens.Jwt;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Api.Services.Email;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// Proves the two halves of the token contract agree — the check the suite was missing.
///
/// <para>Issuing (<c>AuthService</c>) and validating (<c>Program.cs</c>'s
/// <c>TokenValidationParameters</c>) each read the same settings, and both previously carried
/// their own copy of the fallback literals. Changing one default would have made every token this
/// API mints be rejected by the same API. Nothing caught that: the existing auth tests set neither
/// <c>JWT_ISSUER</c> nor <c>JWT_AUDIENCE</c>, so both sides ran on the defaults and agreed by
/// accident rather than by construction.</para>
///
/// <para>These deliberately use <b>non-default</b> values, so agreement has to come from the shared
/// <see cref="JwtSettings"/> rather than from both sides falling back to the same literal.</para>
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class JwtIssueValidateRoundTripTests(PostgreSqlFixture postgres)
{
    private const string SigningKey = "round-trip-signing-key-minimum-32-bytes!!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        var dbOptions = postgres.CreateOptions();
        services.AddScoped(_ => new AppDbContext(dbOptions));
        services.AddIdentityCore<ApplicationUser>(o =>
        {
            o.Password.RequireDigit = false;
            o.Password.RequiredLength = 4;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequireUppercase = false;
            o.User.RequireUniqueEmail = true;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    /// <summary>Issues a real token through the service, using the supplied settings.</summary>
    private static async Task<string> IssueTokenAsync(ServiceProvider provider, JwtSettings jwt)
    {
        // RegisterAsync assigns the Standard role, so the role must exist.
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (await roleManager.FindByNameAsync("Standard") is null)
            await roleManager.CreateAsync(new IdentityRole<Guid>("Standard"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["APP_BASE_URL"] = "https://test.apexracers.gg",
            })
            .Build();

        var refreshTokens = new RefreshTokenStore(
            provider.GetRequiredService<AppDbContext>(), TimeProvider.System, NullLogger<RefreshTokenStore>.Instance);
        var service = new AuthService(
            provider.GetRequiredService<UserManager<ApplicationUser>>(),
            config,
            jwt,
            refreshTokens,
            new FakeEmailSender());

        // Registration hands back nothing now — an account is unusable until its address is
        // confirmed — so the token this test round-trips comes from the sign-in that follows.
        var email = $"driver-{Guid.NewGuid():N}@example.com";
        await service.RegisterAsync(new RegisterRequest(email, "Pass1234"), Ct);

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        await service.ConfirmEmailAsync(
            user!.Id, await userManager.GenerateEmailConfirmationTokenAsync(user), Ct);

        var login = await service.LoginAsync(new LoginRequest(email, "Pass1234"), Ct);
        return login!.Token;
    }

    /// <summary>
    /// The exact parameters Program.cs builds — the same call, not a copy of it. This used to
    /// restate the parameter list by hand, which made it a second place the contract could drift
    /// from the one Program.cs actually uses; <see cref="JwtSettings.ValidationParameters"/> now
    /// owns it for both.
    /// </summary>
    private static TokenValidationParameters ValidationFrom(JwtSettings jwt) =>
        jwt.ValidationParameters();

    [Fact]
    public async Task ATokenIssuedUnderCustomSettingsValidatesUnderTheSameSettings()
    {
        var jwt = new JwtSettings(SigningKey, "custom-issuer", "custom-audience");
        await using var provider = BuildProvider();

        var token = await IssueTokenAsync(provider, jwt);
        var principal = new JwtSecurityTokenHandler()
            .ValidateToken(token, ValidationFrom(jwt), out var validated);

        Assert.NotNull(principal);
        Assert.Equal("custom-issuer", ((JwtSecurityToken)validated).Issuer);
        Assert.Contains("custom-audience", ((JwtSecurityToken)validated).Audiences);
    }

    [Fact]
    public async Task ATokenIssuedUnderTheDefaultsValidatesUnderTheDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JWT_SIGNING_KEY"] = SigningKey })
            .Build();
        var jwt = JwtSettings.FromConfiguration(config);
        await using var provider = BuildProvider();

        var token = await IssueTokenAsync(provider, jwt);

        Assert.NotNull(new JwtSecurityTokenHandler().ValidateToken(token, ValidationFrom(jwt), out _));
    }

    [Fact]
    public async Task AnIssuerMismatchIsRejected()
    {
        // This is the outage the shared binding prevents: one side's default edited, the other's
        // not. Asserting it fails proves the round-trip tests above are not passing vacuously.
        await using var provider = BuildProvider();
        var issued = new JwtSettings(SigningKey, "issuer-a", "shared-audience");
        var validating = new JwtSettings(SigningKey, "issuer-b", "shared-audience");

        var token = await IssueTokenAsync(provider, issued);

        Assert.Throws<SecurityTokenInvalidIssuerException>(
            () => new JwtSecurityTokenHandler().ValidateToken(token, ValidationFrom(validating), out _));
    }

    [Fact]
    public async Task AnAudienceMismatchIsRejected()
    {
        await using var provider = BuildProvider();
        var issued = new JwtSettings(SigningKey, "shared-issuer", "audience-a");
        var validating = new JwtSettings(SigningKey, "shared-issuer", "audience-b");

        var token = await IssueTokenAsync(provider, issued);

        Assert.Throws<SecurityTokenInvalidAudienceException>(
            () => new JwtSecurityTokenHandler().ValidateToken(token, ValidationFrom(validating), out _));
    }

    [Fact]
    public async Task ASigningKeyMismatchIsRejected()
    {
        await using var provider = BuildProvider();
        var issued = new JwtSettings(SigningKey, "shared-issuer", "shared-audience");
        var validating = new JwtSettings(
            "a-completely-different-key-also-32-bytes!", "shared-issuer", "shared-audience");

        var token = await IssueTokenAsync(provider, issued);

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(
            () => new JwtSecurityTokenHandler().ValidateToken(token, ValidationFrom(validating), out _));
    }
}
