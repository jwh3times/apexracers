using System.Net;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Publication;

[Collection(PostgreSqlCollection.Name)]
public sealed class PublicationReleaseAtomicReviewTests(PostgreSqlFixture fixture)
{
    [Fact]
    public Task CompetingHostsCannotBothReviewEmptyHistoryBeforeReservation()
        => PublicationLedgerTopology.Run(fixture, "atomic-review-lock", async topology =>
        {
            await topology.Control("hold/first/review-assessed");
            var first = topology.Read("first", PublicationPurpose.Owner, "owner");
            await topology.Wait("first", "review-assessed");
            Assert.Empty((await topology.History.ReadAsync(topology.Token)).Releases);

            await topology.Control("hold/second/before-admission", secondHost: true);
            var second = topology.Read("second", PublicationPurpose.Owner, "owner2", offset: 1, secondHost: true);
            await topology.Wait("second", "before-admission", secondHost: true);
            await topology.Control("release/second/before-admission", secondHost: true);
            await topology.WaitForCoordinationWaiter();
            Assert.Empty((await topology.History.ReadAsync(topology.Token)).Releases);

            await topology.Control("release/first/review-assessed");
            using var winner = await first;
            using var loser = await second;
            Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
            Assert.Contains("ownerAnalytics", await winner.Content.ReadAsStringAsync(topology.Token));
            await PublicationLedgerTopology.Denied(loser, topology.Token);
            await topology.Wait("first", "checkpointed");

            var entry = Assert.Single((await topology.History.ReadAsync(topology.Token)).Releases);
            Assert.Equal(PublicationPurpose.Owner, entry.Proposal.Purpose);
            Assert.Equal(CohortActors.Owner, entry.Proposal.RecipientUserId);
            Assert.True(entry.Terminal);
            Assert.False(entry.ProvenUnsent);
            await using var db = topology.Db();
            Assert.Equal(1, await db.Set<PublicationRelease>().CountAsync(topology.Token));
        });
}
