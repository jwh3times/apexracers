using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace ApexRacers.Tests.Services;

public class SubjectDriverContextTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static SubjectDriverContext CreateContext(AppDbContext db) =>
        new(db, new FeatureFlagEligibility(db));

    [Fact]
    public async Task GetSubjectDriverCustIdAsync_UserWithClaimedIdentity_ReturnsCustomerId()
    {
        await using var db = DbContextFactory.Create();
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Jerry", IRacingCustomerId = 260514 };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);

        var result = await CreateContext(db).GetSubjectDriverCustIdAsync(user.Id, Ct);

        Assert.Equal(260514, result);
    }

    [Fact]
    public async Task GetSubjectDriverCustIdAsync_UserWithoutClaimedIdentity_ReturnsNull()
    {
        await using var db = DbContextFactory.Create();
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Jerry", IRacingCustomerId = null };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);

        var result = await CreateContext(db).GetSubjectDriverCustIdAsync(user.Id, Ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSubjectDriverCustIdAsync_UnknownUser_ReturnsNull()
    {
        await using var db = DbContextFactory.Create();

        var result = await CreateContext(db).GetSubjectDriverCustIdAsync(Guid.NewGuid(), Ct);

        Assert.Null(result);
    }

    private static void SeedAlphaUserWithDemoFlag(
        AppDbContext db, ApplicationUser user, string roleName, bool demoEnabled)
    {
        var roleId = Guid.NewGuid();
        db.Roles.Add(new IdentityRole<Guid> { Id = roleId, Name = roleName, NormalizedName = roleName.ToUpperInvariant() });
        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleId });
        db.FeatureFlags.Add(new FeatureFlag
        {
            Key = "iracing-demo", Name = "Demo iRacing data",
            MinimumRole = "Alpha", IsEnabled = demoEnabled,
        });
    }

    [Fact]
    public async Task GetSubjectDriverCustIdAsync_DemoFlagOnForAlphaUser_ReturnsDemoDriver()
    {
        await using var db = DbContextFactory.Create();
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Alpha", IRacingCustomerId = null };
        SeedAlphaUserWithDemoFlag(db, user, "Alpha", demoEnabled: true);
        await db.SaveChangesAsync(Ct);

        var result = await CreateContext(db).GetSubjectDriverCustIdAsync(user.Id, Ct);

        Assert.Equal(DemoData.DriverCustId, result);
    }

    [Fact]
    public async Task GetSubjectDriverCustIdAsync_DemoFlagOnButUserBelowMinimumRole_ReturnsClaimedIdentity()
    {
        await using var db = DbContextFactory.Create();
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Std", IRacingCustomerId = 555 };
        SeedAlphaUserWithDemoFlag(db, user, "Standard", demoEnabled: true);
        await db.SaveChangesAsync(Ct);

        var result = await CreateContext(db).GetSubjectDriverCustIdAsync(user.Id, Ct);

        Assert.Equal(555, result); // override does NOT fire — Standard < Alpha
    }

    [Fact]
    public async Task GetSubjectDriverCustIdAsync_DemoFlagDisabled_ReturnsClaimedIdentity()
    {
        await using var db = DbContextFactory.Create();
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Alpha", IRacingCustomerId = 555 };
        SeedAlphaUserWithDemoFlag(db, user, "Alpha", demoEnabled: false);
        await db.SaveChangesAsync(Ct);

        var result = await CreateContext(db).GetSubjectDriverCustIdAsync(user.Id, Ct);

        Assert.Equal(555, result); // flag off — normal resolution
    }

    [Fact]
    public async Task GetRequiredSubjectDriverCustIdAsync_UserWithClaimedIdentity_ReturnsCustomerId()
    {
        await using var db = DbContextFactory.Create();
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Jerry", IRacingCustomerId = 260514 };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);

        var result = await CreateContext(db).GetRequiredSubjectDriverCustIdAsync(user.Id, Ct);

        Assert.Equal(260514, result);
    }

    [Fact]
    public async Task GetRequiredSubjectDriverCustIdAsync_DemoFlagOnForUnlinkedAlphaUser_ReturnsDemoDriver()
    {
        await using var db = DbContextFactory.Create();
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Alpha", IRacingCustomerId = null };
        SeedAlphaUserWithDemoFlag(db, user, "Alpha", demoEnabled: true);
        await db.SaveChangesAsync(Ct);

        var result = await CreateContext(db).GetRequiredSubjectDriverCustIdAsync(user.Id, Ct);

        Assert.Equal(DemoData.DriverCustId, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task GetRequiredSubjectDriverCustIdAsync_MissingOrZeroClaim_ThrowsTypedException(
        long? customerId)
    {
        await using var db = DbContextFactory.Create();
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Jerry", IRacingCustomerId = customerId };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);

        var ex = await Assert.ThrowsAsync<IRacingNotLinkedException>(() =>
            CreateContext(db).GetRequiredSubjectDriverCustIdAsync(user.Id, Ct));

        Assert.Equal("IRACING_NOT_LINKED", IRacingNotLinkedException.Code);
        Assert.Equal(IRacingNotLinkedException.DefaultMessage, ex.Message);
    }

    [Fact]
    public void RequireSubjectDriverCustId_ExplicitZero_ThrowsTypedException()
    {
        using var db = DbContextFactory.Create();

        Assert.Throws<IRacingNotLinkedException>(() =>
            CreateContext(db).RequireSubjectDriverCustId(0));
    }
}
