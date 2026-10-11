using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Tests.Helpers;
using ApexRacers.Tests.Lifecycle;
using ApexRacers.Tests.Publication;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.PrivacyContract;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdmittedCatalogTests(PostgreSqlFixture postgres)
{
    [Theory]
    [InlineData("visitor-signed")]
    [InlineData("owner")]
    [InlineData("owner2")]
    public Task RecordedAdmissionProducesUsefulOutputWithoutPartitioningItsHistory(string context) =>
        PublicationLedgerTopology.Run(postgres, "admitted-" + context, async test =>
        {
            var admission = CatalogAdmission.Verify();
            await using var db = test.Db();
            var source = await new ControlledCohortSqlSource(db, new LifecycleGates()).LoadAsync(test.Token);
            Assert.NotNull(source?.Evidence);
            var original = source.Evidence;
            var before = await test.History.ReadAsync(test.Token);
            Assert.True(before.Available);
            Assert.Empty(before.Releases);
            var purpose = context == "visitor-signed" ? PublicationPurpose.Aggregate : PublicationPurpose.Owner;
            using var response = await test.Read("admitted-positive", purpose, context);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            var body = await response.Content.ReadAsStringAsync(test.Token);
            using var output = JsonDocument.Parse(body);
            Assert.NotEmpty(output.RootElement.GetProperty("hiddenGroups").EnumerateArray());
            if (context != "visitor-signed")
                Assert.Equal(92.85714285714286, output.RootElement.GetProperty("ownerAnalytics").GetProperty("percentileRank").GetDouble(), 10);
            await test.Wait("admitted-positive", "checkpointed");
            using var other = await test.Read("admitted-composition", PublicationPurpose.SignedIn, secondHost: true);
            Assert.Equal(context == "visitor-signed" ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, other.StatusCode);
            var otherBody = await other.Content.ReadAsStringAsync(test.Token);
            if (context == "visitor-signed")
            {
                Assert.Contains("Synthetic Consenting Driver", otherBody);
                await test.Wait("admitted-composition", "checkpointed", secondHost: true);
            }
            using var unknown = await test.Read("admitted-unknown", catalog: "unreviewed-catalog", secondHost: true);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unknown.StatusCode);
            var after = await test.History.ReadAsync(test.Token);
            Assert.True(after.Available);
            Assert.Equal(context == "visitor-signed" ? 2 : 1, after.Releases.Length);
            Assert.All(after.Releases, release => { Assert.True(release.Terminal); Assert.False(release.ProvenUnsent); });
            Assert.Equal(original, (await new ControlledCohortSqlSource(db, new LifecycleGates()).LoadAsync(test.Token))!.Evidence);
            var directory = CatalogAdmission.OutputDirectory("admitted-catalog");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, context + ".json"), JsonSerializer.Serialize(new
            {
                Passed = true, Context = context, Review = admission, OriginalEvidence = original,
                Catalog = WholeCohortCandidates.CatalogId, CatalogRevision = source.Snapshot.CatalogRevision,
                SourceHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(source))),
                Grants = await db.DriverAuthorizationGrants.AsNoTracking().Select(g => new { g.UserId, g.CustomerId, g.Revision, g.PersonalConsentVersion, g.SharingConsentVersion }).ToArrayAsync(test.Token),
                History = after, Hosts = new[] { test.First, test.Second }.Select(host => new { host.ProcessId, host.Incarnation }),
                ExecutedAt = DateTimeOffset.UtcNow,
                Limits = "New isolated fabricated instance; one continuous global history per instance. No Real/provider/production permission. Exact owner Field Size inference retained."
            }, new JsonSerializerOptions { WriteIndented = true }), test.Token);
        });
}
