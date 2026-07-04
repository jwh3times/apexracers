using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApexRacers.Tests.Services;

public class AdminServiceTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>(o =>
        {
            o.Password.RequireDigit = false;
            o.Password.RequiredLength = 4;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequireUppercase = false;
            o.User.RequireUniqueEmail = true;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<AppDbContext>();
        return services.BuildServiceProvider();
    }

    private static AdminService BuildService(ServiceProvider provider) =>
        new(provider.GetRequiredService<UserManager<ApplicationUser>>(),
            provider.GetRequiredService<AppDbContext>());

    private static async Task SeedRolesAsync(ServiceProvider provider)
    {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "Standard", "Beta", "Alpha", "Admin" })
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
    }

    // ── GetAllFlagsAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllFlagsAsync_NoFlags_ReturnsEmpty()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        var result = await svc.GetAllFlagsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllFlagsAsync_ReturnsAllFlagsOrderedByKey()
    {
        await using var provider = BuildProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        db.FeatureFlags.AddRange(
            new FeatureFlag { Key = "z-flag", Name = "Z Flag", MinimumRole = "Standard", IsEnabled = true },
            new FeatureFlag { Key = "a-flag", Name = "A Flag", MinimumRole = "Beta", IsEnabled = false }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var svc = BuildService(provider);

        var result = await svc.GetAllFlagsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal("a-flag", result[0].Key);
        Assert.Equal("z-flag", result[1].Key);
    }

    // ── GetFlagsForRoleAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetFlagsForRoleAsync_Standard_OnlyReturnsStandardLevelFlags()
    {
        await using var provider = BuildProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        db.FeatureFlags.AddRange(
            new FeatureFlag { Key = "std-flag", Name = "Std", MinimumRole = "Standard", IsEnabled = true },
            new FeatureFlag { Key = "disabled-std-flag", Name = "Disabled Std", MinimumRole = "Standard", IsEnabled = false },
            new FeatureFlag { Key = "beta-flag", Name = "Beta", MinimumRole = "Beta", IsEnabled = true },
            new FeatureFlag { Key = "alpha-flag", Name = "Alpha", MinimumRole = "Alpha", IsEnabled = true }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var svc = BuildService(provider);

        // Guest/public read path: returns the enabled Standard flag; excludes the
        // disabled Standard flag and the enabled-but-Alpha-gated flag (the latter must
        // never leak to anonymous callers even though it's IsEnabled).
        var result = await svc.GetFlagsForRoleAsync("Standard", TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("std-flag", result[0].Key);
    }

    [Fact]
    public async Task GetFlagsForRoleAsync_Admin_ReturnsAllEnabledFlags()
    {
        await using var provider = BuildProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        db.FeatureFlags.AddRange(
            new FeatureFlag { Key = "std-flag", Name = "Std", MinimumRole = "Standard", IsEnabled = true },
            new FeatureFlag { Key = "alpha-flag", Name = "Alpha", MinimumRole = "Alpha", IsEnabled = true }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var svc = BuildService(provider);

        var result = await svc.GetFlagsForRoleAsync("Admin", TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetFlagsForRoleAsync_DisabledFlag_IsExcluded()
    {
        await using var provider = BuildProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        db.FeatureFlags.Add(
            new FeatureFlag { Key = "disabled-flag", Name = "Disabled", MinimumRole = "Standard", IsEnabled = false }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var svc = BuildService(provider);

        var result = await svc.GetFlagsForRoleAsync("Admin", TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFlagsForRoleAsync_UnknownRole_TreatedAsStandard()
    {
        await using var provider = BuildProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        db.FeatureFlags.AddRange(
            new FeatureFlag { Key = "std-flag", Name = "Std", MinimumRole = "Standard", IsEnabled = true },
            new FeatureFlag { Key = "beta-flag", Name = "Beta", MinimumRole = "Beta", IsEnabled = true }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var svc = BuildService(provider);

        var result = await svc.GetFlagsForRoleAsync("UnknownRole", TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("std-flag", result[0].Key);
    }

    // ── GetFlagsForUserAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetFlagsForUserAsync_UnknownUser_DefaultsToStandardFlags()
    {
        await using var provider = BuildProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        db.FeatureFlags.Add(new FeatureFlag { Key = "std-flag", Name = "Std", MinimumRole = "Standard", IsEnabled = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var svc = BuildService(provider);

        var result = await svc.GetFlagsForUserAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetFlagsForUserAsync_KnownUserWithBetaRole_ReturnsBetaFlags()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var db = provider.GetRequiredService<AppDbContext>();
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        db.FeatureFlags.AddRange(
            new FeatureFlag { Key = "std-flag", Name = "Std", MinimumRole = "Standard", IsEnabled = true },
            new FeatureFlag { Key = "beta-flag", Name = "Beta", MinimumRole = "Beta", IsEnabled = true }
        );
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var user = new ApplicationUser { DisplayName = "Tester", UserName = "t@t.com", Email = "t@t.com" };
        await userManager.CreateAsync(user, "Pass1234");
        await userManager.AddToRoleAsync(user, "Beta");

        var svc = BuildService(provider);
        var result = await svc.GetFlagsForUserAsync(user.Id, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
    }

    // ── CreateFlagAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateFlagAsync_ValidRequest_ReturnsFlagDto()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        var dto = await svc.CreateFlagAsync(
            new CreateFeatureFlagRequest("new-flag", "New Flag", "desc", true, "Beta"),
            TestContext.Current.CancellationToken);

        Assert.Equal("new-flag", dto.Key);
        Assert.Equal("New Flag", dto.Name);
        Assert.Equal("desc", dto.Description);
        Assert.True(dto.IsEnabled);
        Assert.Equal("Beta", dto.MinimumRole);
    }

    [Fact]
    public async Task CreateFlagAsync_DuplicateKey_Throws()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        await svc.CreateFlagAsync(
            new CreateFeatureFlagRequest("dup-flag", "Dup", null, true, "Standard"),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateFlagAsync(
                new CreateFeatureFlagRequest("dup-flag", "Dup2", null, false, "Standard"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateFlagAsync_InvalidRole_Throws()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateFlagAsync(
                new CreateFeatureFlagRequest("f", "F", null, true, "SuperUser"),
                TestContext.Current.CancellationToken));
    }

    // ── UpdateFlagAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateFlagAsync_UpdatesNameDescriptionEnabledRole()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        var created = await svc.CreateFlagAsync(
            new CreateFeatureFlagRequest("upd-flag", "Old Name", "old desc", false, "Standard"),
            TestContext.Current.CancellationToken);

        var updated = await svc.UpdateFlagAsync(
            created.Id,
            new UpdateFeatureFlagRequest("New Name", "new desc", true, "Beta"),
            TestContext.Current.CancellationToken);

        Assert.Equal("New Name", updated.Name);
        Assert.Equal("new desc", updated.Description);
        Assert.True(updated.IsEnabled);
        Assert.Equal("Beta", updated.MinimumRole);
    }

    [Fact]
    public async Task UpdateFlagAsync_NotFound_Throws()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateFlagAsync(9999, new UpdateFeatureFlagRequest("N", null, true, "Standard"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateFlagAsync_InvalidRole_Throws()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        var created = await svc.CreateFlagAsync(
            new CreateFeatureFlagRequest("r-flag", "R", null, true, "Standard"),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateFlagAsync(created.Id, new UpdateFeatureFlagRequest("R", null, true, "BadRole"),
                TestContext.Current.CancellationToken));
    }

    // ── DeleteFlagAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteFlagAsync_RemovesFlag()
    {
        await using var provider = BuildProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        var svc = BuildService(provider);

        var created = await svc.CreateFlagAsync(
            new CreateFeatureFlagRequest("del-flag", "Del", null, true, "Standard"),
            TestContext.Current.CancellationToken);

        await svc.DeleteFlagAsync(created.Id, TestContext.Current.CancellationToken);

        Assert.False(await db.FeatureFlags.AnyAsync(f => f.Key == "del-flag", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteFlagAsync_NotFound_Throws()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.DeleteFlagAsync(9999, TestContext.Current.CancellationToken));
    }

    // ── GetUsersAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUsersAsync_NoUsers_ReturnsEmpty()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        var result = await svc.GetUsersAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUsersAsync_UserWithRole_ReturnsUserDto()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var svc = BuildService(provider);

        var user = new ApplicationUser { DisplayName = "Test Driver", UserName = "driver@example.com", Email = "driver@example.com" };
        await userManager.CreateAsync(user, "Pass1234");
        await userManager.AddToRoleAsync(user, "Beta");

        var result = await svc.GetUsersAsync(TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal("driver@example.com", dto.Email);
        Assert.Equal("Test Driver", dto.DisplayName);
        Assert.Equal("Beta", dto.Role);
    }

    // ── SetUserRoleAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task SetUserRoleAsync_ValidRole_UpdatesUserRole()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var svc = BuildService(provider);

        var user = new ApplicationUser { DisplayName = "Racer", UserName = "r@example.com", Email = "r@example.com" };
        await userManager.CreateAsync(user, "Pass1234");
        await userManager.AddToRoleAsync(user, "Standard");

        var dto = await svc.SetUserRoleAsync(user.Id, "Alpha", TestContext.Current.CancellationToken);

        Assert.Equal("Alpha", dto.Role);
    }

    [Fact]
    public async Task SetUserRoleAsync_InvalidRole_Throws()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var svc = BuildService(provider);

        var user = new ApplicationUser { DisplayName = "Racer", UserName = "r@example.com", Email = "r@example.com" };
        await userManager.CreateAsync(user, "Pass1234");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SetUserRoleAsync(user.Id, "Superuser", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetUserRoleAsync_AdminUser_Throws()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var svc = BuildService(provider);

        var user = new ApplicationUser { DisplayName = "Admin", UserName = "admin@example.com", Email = "admin@example.com" };
        await userManager.CreateAsync(user, "Pass1234");
        await userManager.AddToRoleAsync(user, "Admin");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SetUserRoleAsync(user.Id, "Standard", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetUserRoleAsync_UserNotFound_Throws()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SetUserRoleAsync(Guid.NewGuid(), "Standard", TestContext.Current.CancellationToken));
    }
}
