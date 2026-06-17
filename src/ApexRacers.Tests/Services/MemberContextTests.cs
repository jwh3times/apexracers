using ApexRacers.Api.Services;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class MemberContextTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetCustIdAsync_UserWithLinkedId_ReturnsCustId()
    {
        await using var db = DbContextFactory.Create();
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Jerry", IRacingCustomerId = 260514 };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);

        var result = await new MemberContext(db).GetCustIdAsync(user.Id, Ct);

        Assert.Equal(260514, result);
    }

    [Fact]
    public async Task GetCustIdAsync_UserWithoutLinkedId_ReturnsNull()
    {
        await using var db = DbContextFactory.Create();
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Jerry", IRacingCustomerId = null };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);

        var result = await new MemberContext(db).GetCustIdAsync(user.Id, Ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCustIdAsync_UnknownUser_ReturnsNull()
    {
        await using var db = DbContextFactory.Create();

        var result = await new MemberContext(db).GetCustIdAsync(Guid.NewGuid(), Ct);

        Assert.Null(result);
    }
}
