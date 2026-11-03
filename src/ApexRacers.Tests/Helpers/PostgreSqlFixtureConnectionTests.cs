using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Helpers;

/// <summary>
/// Issue #391: opening one physical connection per context made a full run leave thousands of
/// client sockets in TIME_WAIT. Contexts over one test database must share physical connections,
/// and a later test must not inherit the earlier test's idle ones.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlFixtureConnectionTests(PostgreSqlFixture fixture)
{
    private const int Operations = 20;

    [Fact]
    public async Task ContextsOverOneTestDatabaseShareAPhysicalConnectionReleasedForTheNextTest()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await fixture.CreateOptionsAsync(ct);
        string database;
        await using (var first = new AppDbContext(options)) database = first.Database.GetDbConnection().Database;
        for (var i = 0; i < Operations; i++)
        {
            await using var db = new AppDbContext(options);
            await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        }

        // The next test's first options request releases this test's pools; until then they stay open.
        fixture.AdoptPoolOwner("next-test");
        var sessions = await fixture.SessionsAfterBackendsExitAsync(database, ct);

        Assert.True(sessions <= 3, $"{Operations} sequential operations opened {sessions} physical sessions.");
    }
}
