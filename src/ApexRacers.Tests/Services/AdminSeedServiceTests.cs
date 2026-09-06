using ApexRacers.Api.Services;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public class AdminSeedServiceTests(PostgreSqlFixture postgres)
{
    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var options = postgres.CreateOptions();
        services.AddScoped(_ => new AppDbContext(options));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<AdminSeedService>();
        return services.BuildServiceProvider();
    }

    private static async Task<ApplicationUser> SeedUserAsync(
        ServiceProvider provider, bool confirmed, string role)
    {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var name in new[] { "Standard", "Alpha", "Admin" })
            Assert.True((await roleManager.CreateAsync(new IdentityRole<Guid>(name))).Succeeded);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = "seed@example.com", Email = "seed@example.com",
            DisplayName = "Seed", EmailConfirmed = confirmed,
        };
        Assert.True((await userManager.CreateAsync(user)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    [Theory]
    [InlineData("Standard")]
    [InlineData("Alpha")]
    public async Task UnconfirmedMatchingAccount_KeepsExistingRole(string role)
    {
        await using var provider = BuildProvider();
        var user = await SeedUserAsync(provider, false, role);

        await provider.GetRequiredService<AdminSeedService>()
            .PromoteConfirmedUsersAsync(user.Email, TestContext.Current.CancellationToken);

        Assert.Equal([role], await provider.GetRequiredService<UserManager<ApplicationUser>>().GetRolesAsync(user));
    }

    [Fact]
    public async Task ConfirmedMatchingAccount_PromotesOnceAndTrimsEmailList()
    {
        await using var provider = BuildProvider();
        var user = await SeedUserAsync(provider, true, "Alpha");
        var service = provider.GetRequiredService<AdminSeedService>();

        await service.PromoteConfirmedUsersAsync(
            " , missing@example.com, seed@example.com , ,seed@example.com, ", TestContext.Current.CancellationToken);
        await service.PromoteConfirmedUsersAsync(user.Email, TestContext.Current.CancellationToken);

        Assert.Equal(["Admin"], await provider.GetRequiredService<UserManager<ApplicationUser>>().GetRolesAsync(user));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingAdmin_IsPreserved(bool confirmed)
    {
        await using var provider = BuildProvider();
        var user = await SeedUserAsync(provider, confirmed, "Admin");
        var originalStamp = user.ConcurrencyStamp;

        await provider.GetRequiredService<AdminSeedService>()
            .PromoteConfirmedUsersAsync(user.Email, TestContext.Current.CancellationToken);

        Assert.Equal(["Admin"], await provider.GetRequiredService<UserManager<ApplicationUser>>().GetRolesAsync(user));
        Assert.Equal(originalStamp, user.ConcurrencyStamp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" , , ")]
    [InlineData("missing@example.com")]
    public async Task MissingConfigurationOrAccount_DoesNotCreateUsers(string? emails)
    {
        await using var provider = BuildProvider();

        await provider.GetRequiredService<AdminSeedService>()
            .PromoteConfirmedUsersAsync(emails, TestContext.Current.CancellationToken);

        Assert.Empty(await provider.GetRequiredService<AppDbContext>().Users.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedAdminAssignment_RollsBackExistingRoleRemoval()
    {
        await using var provider = BuildProvider();
        var user = await SeedUserAsync(provider, true, "Alpha");
        var db = provider.GetRequiredService<AppDbContext>();
        var adminRole = await db.Roles.SingleAsync(r => r.Name == "Admin", TestContext.Current.CancellationToken);
        db.Roles.Remove(adminRole);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<AdminSeedService>()
            .PromoteConfirmedUsersAsync(user.Email, TestContext.Current.CancellationToken));

        db.ChangeTracker.Clear();
        var role = await db.UserRoles.SingleAsync(r => r.UserId == user.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Alpha", (await db.Roles.FindAsync([role.RoleId], TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task IdentityValidationFailure_ThrowsAndPreservesExistingRole()
    {
        await using var provider = BuildProvider();
        var user = await SeedUserAsync(provider, true, "Alpha");
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        userManager.UserValidators.Add(new RejectUpdates());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<AdminSeedService>()
            .PromoteConfirmedUsersAsync(user.Email, TestContext.Current.CancellationToken));

        Assert.Contains("RejectedUpdate", exception.Message);
        provider.GetRequiredService<AppDbContext>().ChangeTracker.Clear();
        Assert.Equal(["Alpha"], await userManager.GetRolesAsync(user));
    }

    private sealed class RejectUpdates : IUserValidator<ApplicationUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "RejectedUpdate" }));
    }
}
