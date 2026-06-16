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

        var result = await svc.RegisterAsync(new RegisterRequest("jerry@example.com", "Pass1234"), TestContext.Current.CancellationToken);

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

        await svc.RegisterAsync(new RegisterRequest("dup@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RegisterAsync(new RegisterRequest("dup@example.com", "Pass1234"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterAsync_Token_ContainsEmailAndNameClaims()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var result = await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"), TestContext.Current.CancellationToken);

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

        var result = await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"), TestContext.Current.CancellationToken);

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

        await svc.RegisterAsync(new RegisterRequest("user@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        var result = await svc.LoginAsync(new LoginRequest("user@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotEmpty(result!.Token);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsNull()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await svc.RegisterAsync(new RegisterRequest("user@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        var result = await svc.LoginAsync(new LoginRequest("user@example.com", "WrongPassword"), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_ReturnsNull()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        var result = await svc.LoginAsync(new LoginRequest("nobody@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    // ── UpdateProfileAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProfileAsync_UpdatesDisplayName()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("New Name"), TestContext.Current.CancellationToken);

        Assert.Equal("New Name", result.DisplayName);
    }

    [Fact]
    public async Task UpdateProfileAsync_SavesIRacingCustomerId_WhenProvided()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", 123456789L), TestContext.Current.CancellationToken);

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

        var reg = await svc.RegisterAsync(new RegisterRequest("old@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", Email: "new@example.com"), TestContext.Current.CancellationToken);

        var canLoginWithNew = await svc.LoginAsync(new LoginRequest("new@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        Assert.NotNull(canLoginWithNew);

        var canLoginWithOld = await svc.LoginAsync(new LoginRequest("old@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        Assert.Null(canLoginWithOld);
    }

    [Fact]
    public async Task UpdateProfileAsync_Token_ContainsUpdatedEmailClaim()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("old@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", Email: "new@example.com"), TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "new@example.com");
    }

    [Fact]
    public async Task UpdateProfileAsync_DuplicateEmail_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg1 = await svc.RegisterAsync(new RegisterRequest("user1@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        await svc.RegisterAsync(new RegisterRequest("user2@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateProfileAsync(reg1.UserId, new UpdateProfileRequest("Name", Email: "user2@example.com"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateProfileAsync_SameEmail_DoesNotThrow()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("user@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", Email: "user@example.com"), TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task UpdateProfileAsync_EmptyDisplayName_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("   "), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateProfileAsync_UnknownUser_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateProfileAsync(Guid.NewGuid(), new UpdateProfileRequest("Name"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateProfileAsync_ValidThemePreference_SetsThemeClaim()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", ThemePreference: "dark"), TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "theme_preference" && c.Value == "dark");
    }

    [Fact]
    public async Task UpdateProfileAsync_InvalidThemePreference_LeavesDefaultTheme()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        // "neon" is not a valid theme — the second condition is false, so the default "auto" is preserved.
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", ThemePreference: "neon"), TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "theme_preference" && c.Value == "auto");
    }

    [Fact]
    public async Task UpdateProfileAsync_DoesNotClearIRacingCustomerId_WhenNotProvided()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", 100042L), TestContext.Current.CancellationToken);

        // Second update without IRacingCustomerId — should not clear the first one
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name Two"), TestContext.Current.CancellationToken);

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

        var result = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);

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

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var result = await svc.UpdateRoleAsync(reg.UserId, "Beta", TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "Beta");
    }

    [Fact]
    public async Task UpdateRoleAsync_BackToStandard_ReturnsTokenWithStandardRole()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        await svc.UpdateRoleAsync(reg.UserId, "Alpha", TestContext.Current.CancellationToken);
        var result = await svc.UpdateRoleAsync(reg.UserId, "Standard", TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "Standard");
    }

    [Fact]
    public async Task UpdateRoleAsync_ToAdmin_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateRoleAsync(reg.UserId, "Admin", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateRoleAsync_AdminCannotSelfDemote()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var reg = await svc.RegisterAsync(new RegisterRequest("admin@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        // Promote to Admin via UserManager directly (as the startup seed would)
        var user = await userManager.FindByIdAsync(reg.UserId.ToString());
        var roles = await userManager.GetRolesAsync(user!);
        await userManager.RemoveFromRolesAsync(user!, roles);
        await userManager.AddToRoleAsync(user!, "Admin");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateRoleAsync(reg.UserId, "Standard", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateRoleAsync_UnknownUser_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        // "Beta" is a valid role, so the role-validation check passes and the
        // failure must come from the missing user lookup.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateRoleAsync(Guid.NewGuid(), "Beta", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateRoleAsync_UnsupportedRole_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateRoleAsync(reg.UserId, "Superuser", TestContext.Current.CancellationToken));
    }

    // ── UpdateThemeAsync ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("auto")]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task UpdateThemeAsync_ValidTheme_SetsThemePreferenceClaim(string theme)
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var result = await svc.UpdateThemeAsync(reg.UserId, theme, TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "theme_preference" && c.Value == theme);
    }

    [Fact]
    public async Task UpdateThemeAsync_InvalidTheme_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await svc.RegisterAsync(new RegisterRequest("u@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateThemeAsync(reg.UserId, "neon", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateThemeAsync_UnknownUser_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        // "dark" is valid, so the failure must come from the missing user lookup.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateThemeAsync(Guid.NewGuid(), "dark", TestContext.Current.CancellationToken));
    }

    // ── HandleCallbackAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task HandleCallbackAsync_ThrowsNotImplementedException()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            svc.HandleCallbackAsync("code", "state", TestContext.Current.CancellationToken));
    }

    // ── RefreshAsync / RevokeAsync ────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_ReturnsRefreshToken()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var result = await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        Assert.NotNull(result.RefreshToken);
        Assert.NotEmpty(result.RefreshToken!);
    }

    [Fact]
    public async Task LoginAsync_CorrectCredentials_ReturnsRefreshToken()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var result = await svc.LoginAsync(new LoginRequest("driver@example.com", "Pass1234"), TestContext.Current.CancellationToken);

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

        var reg     = await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var originalRefresh = reg.RefreshToken!;

        var refreshed = await svc.RefreshAsync(originalRefresh, TestContext.Current.CancellationToken);

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

        var reg     = await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var oldRefresh = reg.RefreshToken!;

        // Rotate once — this revokes oldRefresh
        await svc.RefreshAsync(oldRefresh, TestContext.Current.CancellationToken);

        // Re-using the old token must throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshAsync(oldRefresh, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefreshAsync_InvalidToken_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshAsync("this-token-does-not-exist", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RevokeAsync_ValidToken_SubsequentRefreshThrows()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var login   = await svc.LoginAsync(
            (await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"), TestContext.Current.CancellationToken) is { }
                ? new LoginRequest("driver@example.com", "Pass1234")
                : throw new InvalidOperationException()),
            TestContext.Current.CancellationToken);

        var refreshToken = login!.RefreshToken!;

        await svc.RevokeAsync(refreshToken, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshAsync(refreshToken, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RevokeAsync_UnknownToken_DoesNotThrow()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        // Should complete without throwing — unknown tokens are silently ignored
        await svc.RevokeAsync("garbage", TestContext.Current.CancellationToken);
    }
}
