using ApexRacers.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Data;

/// <summary>
/// Locks the single-role-per-user invariant at the DB level: the unique index on
/// <c>UserRoles.UserId</c> (configured in <see cref="ApexRacers.Data.AppDbContext"/>) must reject a
/// second role for the same user. The app maintains one role per user through Remove-then-Add swaps;
/// this constraint defends the invariant against any path — or manual edit — that would bypass that.
/// </summary>
public class UserRoleConstraintTests
{
    private static IdentityUserRole<Guid> Link(Guid userId, Guid roleId) =>
        new() { UserId = userId, RoleId = roleId };

    [Fact]
    public async Task Second_role_for_the_same_user_is_rejected()
    {
        await using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid();
        var standard = new IdentityRole<Guid>("Standard") { Id = Guid.NewGuid() };
        var admin = new IdentityRole<Guid>("Admin") { Id = Guid.NewGuid() };
        db.Roles.AddRange(standard, admin);
        db.UserRoles.Add(Link(userId, standard.Id));
        await db.SaveChangesAsync();

        db.UserRoles.Add(Link(userId, admin.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_single_role_per_user_is_allowed()
    {
        await using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid();
        var standard = new IdentityRole<Guid>("Standard") { Id = Guid.NewGuid() };
        db.Roles.Add(standard);
        db.UserRoles.Add(Link(userId, standard.Id));
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.UserRoles.CountAsync(ur => ur.UserId == userId));
    }
}
