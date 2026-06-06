using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApexRacers.Tests.Services;

public class AuthServiceTests
{
    // ── Shared setup ─────────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>(o =>
        {
            o.Password.RequireDigit          = false;
            o.Password.RequiredLength        = 4;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequireUppercase      = false;
            o.User.RequireUniqueEmail        = true;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<AppDbContext>();
        return services.BuildServiceProvider();
    }

    private static AuthService BuildService(ServiceProvider provider)
    {
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var db          = provider.GetRequiredService<AppDbContext>();
        var config      = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_SIGNING_KEY"] = "unit-test-signing-key-minimum-32-bytes-long!"
            })
            .Build();
        return new AuthService(userManager, config, db);
    }

    private static async Task SeedRolesAsync(ServiceProvider provider)
    {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "Standard", "Beta", "Alpha", "Admin" })
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
    }

    // ── RegisterAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_NewUser_ReturnsTokenAndSetsDisplayNameFromEmail()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var result = await svc.RegisterAsync(new RegisterRequest("jerry@example.com", "Pass1234"));

        Assert.NotEmpty(result.Token);
        Assert.Equal("jerry", result.DisplayName);
        Assert.NotEqual(Guid.Empty, result.UserId);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await svc.RegisterAsync(new RegisterRequest("dup@example.com", "Pass1234"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RegisterAsync(new RegisterRequest("dup@example.com", "Pass1234")));
    }

    [Fact]
    public async Task RegisterAsync_Token_ContainsEmailAndNameClaims()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var result = await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);

        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Sub   && Guid.TryParse(c.Value, out _));
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "driver@example.com");
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Name  && c.Value == "driver");
    }

    [Fact]
    public async Task RegisterAsync_Token_ContainsStandardRoleClaim()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var result = await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "Standard");
    }

    // ── LoginAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_CorrectCredentials_ReturnsAuthResult()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await svc.RegisterAsync(new RegisterRequest("user@example.com", "Pass1234"));

        var result = await svc.LoginAsync(new LoginRequest("user@example.com", "Pass1234"));

        Assert.NotNull(result);
        Assert.NotEmpty(result!.Token);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsNull()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await svc.RegisterAsync(new RegisterRequest("user@example.com", "Pass1234"));

        var result = await svc.LoginAsync(new LoginRequest("user@example.com", "WrongPassword"));

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_ReturnsNull()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        var result = await svc.LoginAsync(new LoginRequest("nobody@example.com", "Pass1234"));

        Assert.Null(result);
    }

    // ── UpdateProfileAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProfileAsync_UpdatesDisplayName()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"));
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("New Name"));

        Assert.Equal("New Name", result.DisplayName);
    }

    [Fact]
    public async Task UpdateProfileAsync_SavesIRacingCustomerId_WhenProvided()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"));
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", 123456789L));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "iracing_id" && c.Value == "123456789");
    }

    [Fact]
    public async Task UpdateProfileAsync_UpdatesEmail_WhenNewEmailProvided()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("old@example.com", "Pass1234"));
        await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", Email: "new@example.com"));

        var canLoginWithNew = await svc.LoginAsync(new LoginRequest("new@example.com", "Pass1234"));
        Assert.NotNull(canLoginWithNew);

        var canLoginWithOld = await svc.LoginAsync(new LoginRequest("old@example.com", "Pass1234"));
        Assert.Null(canLoginWithOld);
    }

    [Fact]
    public async Task UpdateProfileAsync_Token_ContainsUpdatedEmailClaim()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("old@example.com", "Pass1234"));
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", Email: "new@example.com"));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "new@example.com");
    }

    [Fact]
    public async Task UpdateProfileAsync_DuplicateEmail_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg1 = await svc.RegisterAsync(new RegisterRequest("user1@example.com", "Pass1234"));
        await svc.RegisterAsync(new RegisterRequest("user2@example.com", "Pass1234"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateProfileAsync(reg1.UserId, new UpdateProfileRequest("Name", Email: "user2@example.com")));
    }

    [Fact]
    public async Task UpdateProfileAsync_SameEmail_DoesNotThrow()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("user@example.com", "Pass1234"));
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", Email: "user@example.com"));

        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task UpdateProfileAsync_DoesNotClearIRacingCustomerId_WhenNotProvided()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"));
        await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", 100042L));

        // Second update without IRacingCustomerId — should not clear the first one
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name Two"));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "iracing_id" && c.Value == "100042");
    }

    [Fact]
    public async Task RegisterAsync_Token_DoesNotContainIRacingIdClaim_ForNewUser()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var result = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "iracing_id");
    }

    // ── UpdateRoleAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRoleAsync_ToBeta_ReturnsTokenWithBetaRole()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"));
        var result = await svc.UpdateRoleAsync(reg.UserId, "Beta");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "Beta");
    }

    [Fact]
    public async Task UpdateRoleAsync_BackToStandard_ReturnsTokenWithStandardRole()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"));
        await svc.UpdateRoleAsync(reg.UserId, "Alpha");
        var result = await svc.UpdateRoleAsync(reg.UserId, "Standard");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "Standard");
    }

    [Fact]
    public async Task UpdateRoleAsync_ToAdmin_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateRoleAsync(reg.UserId, "Admin"));
    }

    [Fact]
    public async Task UpdateRoleAsync_AdminCannotSelfDemote()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var reg = await svc.RegisterAsync(new RegisterRequest("admin@example.com", "Pass1234"));

        // Promote to Admin via UserManager directly (as the startup seed would)
        var user = await userManager.FindByIdAsync(reg.UserId.ToString());
        var roles = await userManager.GetRolesAsync(user!);
        await userManager.RemoveFromRolesAsync(user!, roles);
        await userManager.AddToRoleAsync(user!, "Admin");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateRoleAsync(reg.UserId, "Standard"));
    }

    // ── HandleCallbackAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task HandleCallbackAsync_ThrowsNotImplementedException()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            svc.HandleCallbackAsync("code", "state"));
    }

    // ── RefreshAsync / RevokeAsync ────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_ReturnsRefreshToken()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var result = await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"));

        Assert.NotNull(result.RefreshToken);
        Assert.NotEmpty(result.RefreshToken!);
    }

    [Fact]
    public async Task LoginAsync_CorrectCredentials_ReturnsRefreshToken()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"));
        var result = await svc.LoginAsync(new LoginRequest("driver@example.com", "Pass1234"));

        Assert.NotNull(result);
        Assert.NotNull(result!.RefreshToken);
        Assert.NotEmpty(result.RefreshToken!);
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_ReturnsNewAccessAndRefreshTokens()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg     = await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"));
        var originalRefresh = reg.RefreshToken!;

        var refreshed = await svc.RefreshAsync(originalRefresh);

        // Access token must be non-empty
        Assert.NotEmpty(refreshed.Token);
        // New refresh token must be non-empty and different from the one that was rotated out
        Assert.NotNull(refreshed.RefreshToken);
        Assert.NotEmpty(refreshed.RefreshToken!);
        Assert.NotEqual(originalRefresh, refreshed.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_RotatesToken_OldTokenIsRevoked()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg     = await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"));
        var oldRefresh = reg.RefreshToken!;

        // Rotate once — this revokes oldRefresh
        await svc.RefreshAsync(oldRefresh);

        // Re-using the old token must throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshAsync(oldRefresh));
    }

    [Fact]
    public async Task RefreshAsync_InvalidToken_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshAsync("this-token-does-not-exist"));
    }

    [Fact]
    public async Task RevokeAsync_ValidToken_SubsequentRefreshThrows()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var login   = await svc.LoginAsync(
            (await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234")) is { }
                ? new LoginRequest("driver@example.com", "Pass1234")
                : throw new InvalidOperationException()));

        var refreshToken = login!.RefreshToken!;

        await svc.RevokeAsync(refreshToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshAsync(refreshToken));
    }

    [Fact]
    public async Task RevokeAsync_UnknownToken_DoesNotThrow()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        // Should complete without throwing — unknown tokens are silently ignored
        await svc.RevokeAsync("garbage");
    }
}
