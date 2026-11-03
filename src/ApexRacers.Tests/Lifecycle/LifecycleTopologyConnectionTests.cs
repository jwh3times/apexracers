using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Lifecycle;

/// <summary>
/// Issue #480: after #391 the lifecycle topology produced over a quarter of a full run's physical
/// sessions. The coordinator host and the test process must share connections per database and
/// leave none behind once the topology is disposed.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class LifecycleTopologyConnectionTests(PostgreSqlFixture fixture)
{
    private const int Operations = 20;

    [Fact]
    public async Task CoordinatorAndTestProcessShareConnectionsAndReleaseThemOnDisposal()
    {
        string primary = "", journal = "";
        await LifecycleTopology.RunAsync(fixture, "connection-pooling", [], async test =>
        {
            primary = new NpgsqlConnectionStringBuilder(test.PrimaryConnection).Database!;
            journal = new NpgsqlConnectionStringBuilder(test.JournalConnection).Database!;
            await test.GrantAsync();
            // Only the coordinator is driven: the publisher reaches the primary through the
            // partitionable relay, which stays unpooled by design.
            for (var i = 0; i < Operations; i++)
            {
                using (var read = await test.ReadAsync("pooled-" + i, onCoordinator: true))
                {
                    read.EnsureSuccessStatusCode();
                    await read.Content.ReadAsStringAsync(test.CancellationToken);
                }
                await test.WaitAsync("pooled-" + i, "checkpointed", onCoordinator: true);
                await using var db = test.OpenPrimary();
                await db.Users.CountAsync(test.CancellationToken);
                await test.Journal.ReadUserAsync(SyntheticLifecycleActors.Owner, test.CancellationToken);
            }
        });

        var sessions = await fixture.SessionsAfterBackendsExitAsync(primary, TestContext.Current.CancellationToken)
            + await fixture.SessionsAfterBackendsExitAsync(journal, TestContext.Current.CancellationToken);

        // Two participants (the coordinator and the test process) over two databases. Pooled, each
        // measured one session per database; the bound allows a second for an overlapping open.
        // Unpooled, the same workload opened 385.
        Assert.True(sessions <= 8, $"{Operations * 3} sequential operations opened {sessions} physical sessions.");
    }
}
