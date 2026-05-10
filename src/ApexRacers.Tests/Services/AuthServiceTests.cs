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
        .AddEntityFrameworkStores<AppDbContext>();
        return services.BuildServiceProvider();
    }

    private static AuthService BuildService(ServiceProvider provider)
    {
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_SIGNING_KEY"] = "unit-test-signing-key-minimum-32-bytes-long!"
            })
            .Build();
        return new AuthService(userManager, config);
    }

    // ── RegisterAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_NewUser_ReturnsTokenAndSetsDisplayNameFromEmail()
    {
        await using var provider = BuildProvider();
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
        var svc = BuildService(provider);

        await svc.RegisterAsync(new RegisterRequest("dup@example.com", "Pass1234"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RegisterAsync(new RegisterRequest("dup@example.com", "Pass1234")));
    }

    [Fact]
    public async Task RegisterAsync_Token_ContainsEmailAndNameClaims()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        var result = await svc.RegisterAsync(new RegisterRequest("driver@example.com", "Pass1234"));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);

        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Sub   && Guid.TryParse(c.Value, out _));
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "driver@example.com");
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Name  && c.Value == "driver");
    }

    // ── LoginAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_CorrectCredentials_ReturnsAuthResult()
    {
        await using var provider = BuildProvider();
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

    // ── HandleCallbackAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task HandleCallbackAsync_ThrowsNotImplementedException()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            svc.HandleCallbackAsync("code", "state"));
    }
}
