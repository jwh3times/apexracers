using System.Net;
using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using ApexRacers.Tests.References;
using Xunit;

namespace ApexRacers.Tests.Operating;

[Collection(PostgreSqlCollection.Name)]
public sealed class DriverOperatingHttpTests(PostgreSqlFixture postgres)
{
    [Fact]
    public Task DirectAcquisitionUsesTheSameSharedBudgetAsBackgroundCollection() =>
        ReferenceTopology.Run(postgres, "operating-acquisition", async test =>
        {
            await using var db = test.Db();
            var clock = new ReferenceClock();
            var controls = new SyntheticDriverOperatingStore(db, clock);
            var old = (await controls.ReadAsync(test.Token))!;
            var manifest = old.Manifest with { ScopeVersion = 2,
                Scopes = [new("synthetic-acquisition", "active-season:42", OperatingWork.Acquisition),
                    new("synthetic-acquisition", "active-season:42", OperatingWork.BackgroundCollection)],
                Budget = old.Manifest.Budget! with { AcquisitionUnits = 1 } };
            Assert.True(await controls.ChangeScopeAsync(manifest, new(DriverOperatingStage.Smoke, clock.GetUtcNow(),
                "synthetic-acquisition-review", true, true, true, ScopeVersion: 2), test.Token));
            Assert.True(await controls.OptInAsync(ReferenceActors.Recipient, 2, test.Token));
            await test.Time(clock.GetUtcNow());
            using var permitted = await test.Request("/synthetic/acquisition", "acquisition");
            Assert.Equal(HttpStatusCode.OK, permitted.StatusCode);
            Assert.Contains("synthetic-field", await permitted.Content.ReadAsStringAsync(test.Token));
            using var spent = await test.Request("/synthetic/acquisition", "spent-acquisition", secondHost: true);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, spent.StatusCode);
            await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => new DriverOperatingCollection(controls).CollectAsync<string>(
                new("synthetic-acquisition", "active-season:42", OperatingWork.BackgroundCollection, DataProvenance.Demo, null, OperatingAudience.Visitor),
                _ => throw new InvalidOperationException("API acquisition already spent the shared allowance"), test.Token));
        });

    [Fact]
    public Task PilotEnrollmentCannotBeBypassedByAdminTierOrRequestParameters() =>
        ReferenceTopology.Run(postgres, "operating-pilot", async test =>
        {
            await using var db = test.Db();
            var clock = new ReferenceClock();
            var controls = new SyntheticDriverOperatingStore(db, clock);
            var old = (await controls.ReadAsync(test.Token))!;
            var manifest = old.Manifest with { ScopeVersion = 2, Allowlist = [ReferenceActors.Target] };
            Assert.True(await controls.ChangeScopeAsync(manifest, new(DriverOperatingStage.Smoke, clock.GetUtcNow(),
                "synthetic-scope-review", true, true, true, ScopeVersion: 2), test.Token));
            Assert.True(await controls.OptInAsync(ReferenceActors.Recipient, 2, test.Token));
            using var denied = await test.Request("/api/drivers/scoped/discovery?actor=" + ReferenceActors.Target + "&preview=Alpha", "admin-denied", role: "Admin");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
            manifest = manifest with { ScopeVersion = 3, Allowlist = [ReferenceActors.Recipient] };
            Assert.True(await controls.ChangeScopeAsync(manifest, new(DriverOperatingStage.Smoke, clock.GetUtcNow(),
                "synthetic-reviewed-pilot", true, true, true, ScopeVersion: 3), test.Token));
            using var stale = await test.Request("/api/drivers/scoped/discovery", "old-optin-denied");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, stale.StatusCode);
            Assert.True(await controls.OptInAsync(ReferenceActors.Recipient, 3, test.Token));
            Assert.True(await controls.PromoteAsync(new(DriverOperatingStage.Pilot, clock.GetUtcNow(), "synthetic-pilot", true, true, true, ScopeVersion: 3), test.Token));
            // Synchronize synthetic observation clocks on both hosts, never wall-clock sleep.
            await test.Time(clock.GetUtcNow());
            await test.Discover("allowed-pilot");
        });

    [Fact]
    public Task PreparedOutputCannotDispatchAfterAnOperatingStopAndRetainsReleaseHistory() =>
        ReferenceTopology.Run(postgres, "operating-prepared-stop", async test =>
        {
            await test.Control("hold/delayed/admitted");
            var pending = test.Request("/api/drivers/scoped/discovery", "delayed");
            await test.Wait("delayed", "admitted");
            await using var db = test.Db();
            var clock = new ReferenceClock();
            Assert.True(await new SyntheticDriverOperatingStore(db, clock).StopAsync(new(Guid.NewGuid(), "missed-cleanup", clock.GetUtcNow(), true), test.Token));
            await test.Control("release/delayed/admitted");
            using var response = await pending;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.DoesNotContain("Synthetic Reference Driver", await response.Content.ReadAsStringAsync(test.Token));
            await test.Wait("delayed", "checkpointed");
            var accounting = Assert.Single((await test.History.ReadAsync(test.Token)).Releases);
            Assert.True(accounting.Terminal); Assert.True(accounting.ProvenUnsent);
        });

    [Fact]
    public Task SafetyStopClosesDirectPublicationOnBothHostsButWithdrawalStillCompletes() =>
        ReferenceTopology.Run(postgres, "operating-stop", async test =>
        {
            await test.Discover("useful-before-stop");
            await using var db = test.Db();
            var clock = new ReferenceClock();
            var controls = new SyntheticDriverOperatingStore(db, clock);
            Assert.True(await controls.StopAsync(new(Guid.NewGuid(), "lost-accounting", clock.GetUtcNow(), true), test.Token));
            using var first = await test.Request("/api/drivers/scoped/discovery", "closed-first");
            using var second = await test.Request("/api/drivers/scoped/discovery", "closed-second", secondHost: true);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
            Assert.Equal("no-store", first.Headers.CacheControl!.ToString());
            Assert.True((await test.Transition(ReferenceActors.Target, DriverLifecycleKind.WithdrawSharing)).Completed);
        });
}
