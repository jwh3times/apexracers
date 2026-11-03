using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.References;

/// <summary>
/// Issue #391: the reference topology produced almost a third of a full run's physical sessions.
/// Its two hosts and the test process must share connections per database and leave none behind
/// once the topology is disposed.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class ReferenceTopologyConnectionTests(PostgreSqlFixture fixture)
{
    private const int Operations = 20;

    [Fact]
    public async Task HostsAndTestProcessShareConnectionsAndReleaseThemOnDisposal()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        string primary, independent;
        await using (var test = await ReferenceTopology.CreateAsync(fixture.Container.GetConnectionString(), ct))
        {
            primary = new NpgsqlConnectionStringBuilder(test.PrimaryConnection).Database!;
            independent = new NpgsqlConnectionStringBuilder(test.IndependentConnection).Database!;
            for (var i = 0; i < Operations; i++)
            {
                await test.Control("cleanup");
                await test.Control("cleanup", secondHost: true);
                await using var db = test.Db();
                await db.Users.CountAsync(ct);
            }
        }

        var sessions = await fixture.SessionsAfterBackendsExitAsync(primary, ct)
            + await fixture.SessionsAfterBackendsExitAsync(independent, ct);

        // The two hosts and the test process each hold a small pool per database.
        Assert.True(sessions <= 8, $"{Operations * 3} sequential operations opened {sessions} physical sessions.");
    }
}
