using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace ApexRacers.Tests.Services;

public class FeatureFlagEligibilityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RoleAndUserQueriesApplyTheSameEnabledAndMinimumRoleRules()
    {
        await using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, DisplayName = "Beta" });
        db.Roles.Add(new IdentityRole<Guid> { Id = roleId, Name = "Beta", NormalizedName = "BETA" });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
        db.FeatureFlags.AddRange(
            new FeatureFlag { Key = "standard", Name = "Standard", MinimumRole = "Standard", IsEnabled = true },
            new FeatureFlag { Key = "beta", Name = "Beta", MinimumRole = "Beta", IsEnabled = true },
            new FeatureFlag { Key = "beta-lower", Name = "Beta lowercase", MinimumRole = "beta", IsEnabled = true },
            new FeatureFlag { Key = "alpha", Name = "Alpha", MinimumRole = "Alpha", IsEnabled = true },
            new FeatureFlag { Key = "disabled", Name = "Disabled", MinimumRole = "Standard", IsEnabled = false });
        await db.SaveChangesAsync(Ct);
        var eligibility = new FeatureFlagEligibility(db);

        var forRole = await eligibility.GetActiveForRoleAsync("Beta", Ct);
        var forUser = await eligibility.GetActiveForUserAsync(userId, Ct);

        Assert.Equal(["beta", "beta-lower", "standard"], forRole.Select(f => f.Key).Order());
        Assert.Equal(["beta", "beta-lower", "standard"], forUser.Select(f => f.Key).Order());
        Assert.True(await eligibility.IsActiveForUserAsync("beta", userId, Ct));
        Assert.True(await eligibility.IsActiveForUserAsync("beta-lower", userId, Ct));
        Assert.False(await eligibility.IsActiveForUserAsync("alpha", userId, Ct));
        Assert.False(await eligibility.IsActiveForUserAsync("disabled", userId, Ct));
    }

    [Fact]
    public async Task UnknownUserReceivesStandardEligibility()
    {
        await using var db = DbContextFactory.Create();
        db.FeatureFlags.AddRange(
            new FeatureFlag { Key = "standard", Name = "Standard", MinimumRole = "Standard", IsEnabled = true },
            new FeatureFlag { Key = "beta", Name = "Beta", MinimumRole = "Beta", IsEnabled = true });
        await db.SaveChangesAsync(Ct);
        var eligibility = new FeatureFlagEligibility(db);

        var flags = await eligibility.GetActiveForUserAsync(Guid.NewGuid(), Ct);

        Assert.Equal("standard", Assert.Single(flags).Key);
    }

    [Fact]
    public async Task UnknownMinimumRole_IsNeverEligible()
    {
        await using var db = DbContextFactory.Create();
        db.FeatureFlags.Add(new FeatureFlag
        {
            Key = "unknown-minimum",
            Name = "Unknown minimum",
            MinimumRole = "SuperUser",
            IsEnabled = true,
        });
        await db.SaveChangesAsync(Ct);
        var eligibility = new FeatureFlagEligibility(db);

        var forAdmin = await eligibility.GetActiveForRoleAsync("Admin", Ct);
        var activeForUnknownUser = await eligibility.IsActiveForUserAsync(
            "unknown-minimum",
            Guid.NewGuid(),
            Ct);

        Assert.Empty(forAdmin);
        Assert.False(activeForUnknownUser);
    }
}
