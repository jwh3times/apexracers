using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Publication;

/// <summary>
/// Issue #480: after #391 the publication-ledger topology produced over a third of a full run's
/// physical sessions. Its two hosts and the test process must share connections per database and
/// leave none behind once the topology is disposed.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PublicationLedgerTopologyConnectionTests(PostgreSqlFixture fixture)
{
    private const int Operations = 20;

    [Fact]
    public async Task HostsAndTestProcessShareConnectionsAndReleaseThemOnDisposal()
    {
        string primary = "", history = "";
        await PublicationLedgerTopology.Run(fixture, "connection-pooling", async test =>
        {
            await using (var db = test.Db())
                primary = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database!;
            history = new NpgsqlConnectionStringBuilder(test.IndependentConnection).Database!;
            for (var i = 0; i < Operations; i++)
            {
                // Reconciliation reads the primary and the separate history database on each host.
                foreach (var host in new[] { test.First, test.Second })
                {
                    using var response = await host.ControlClient.PostAsync("/reconcile", null, test.Token);
                    response.EnsureSuccessStatusCode();
                }
                await using var db = test.Db();
                await db.Users.CountAsync(test.Token);
                await test.History.ReadAsync(test.Token);
            }
        });

        var sessions = await fixture.SessionsAfterBackendsExitAsync(primary, TestContext.Current.CancellationToken)
            + await fixture.SessionsAfterBackendsExitAsync(history, TestContext.Current.CancellationToken);

        // Three participants (two hosts and the test process) over two databases. Pooled, each
        // measured one session per database; the bound allows a second for an overlapping open.
        // Unpooled, the same workload opened 159.
        Assert.True(sessions <= 12, $"{Operations * 4} sequential operations opened {sessions} physical sessions.");
    }
}
