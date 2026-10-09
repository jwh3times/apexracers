using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ApexRacers.Tests.Operating;

[Collection(PostgreSqlCollection.Name)]
public sealed class DriverOperatingStoreTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task IndependentCallersShareOneBudgetAndRestartCannotResetIt()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        var clock = new OperatingClock();
        await using var db = new AppDbContext(options);
        var first = new SyntheticDriverOperatingStore(db, clock);
        var pilot = DriverOperatingPolicyTests.Pilot();
        await first.InitializeAsync(pilot.Manifest with { Budget = pilot.Manifest.Budget! with { PublicationUnits = 1 } },
            pilot.Approvals[0] with { At = clock.GetUtcNow() }, Ct);
        await first.OptInAsync(pilot.Manifest.Allowlist[0], 1, Ct);
        Assert.True(await first.PromoteAsync(pilot.Approvals[1], Ct));
        await using var otherDb = new AppDbContext(options);
        var other = new SyntheticDriverOperatingStore(otherDb, clock);
        var request = DriverOperatingPolicyTests.Request(pilot.Manifest.Allowlist[0]);
        var outcomes = await Task.WhenAll(first.ReserveAsync(request, Ct), other.ReserveAsync(request, Ct));
        Assert.Single(outcomes, o => o is not null);
        await using var restartedDb = new AppDbContext(options);
        var restarted = new SyntheticDriverOperatingStore(restartedDb, clock);
        Assert.Null(await restarted.ReserveAsync(request, Ct));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.NotNull(await restarted.ReserveAsync(request, Ct));
    }

    [Fact]
    public async Task BackgroundCollectionStopsBeforeFetchAndRejectsCompletionAfterSafetyStop()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        var clock = new OperatingClock();
        await using var db = new AppDbContext(options);
        var controls = new SyntheticDriverOperatingStore(db, clock);
        var pilot = DriverOperatingPolicyTests.Pilot();
        var manifest = pilot.Manifest with { Scopes = [new("active-season", "season:42", OperatingWork.BackgroundCollection)] };
        await controls.InitializeAsync(manifest, pilot.Approvals[0] with { At = clock.GetUtcNow() }, Ct);
        await controls.PromoteAsync(pilot.Approvals[1], Ct);
        var collection = new DriverOperatingCollection(controls);
        var request = new OperatingRequest("active-season", "season:42", OperatingWork.BackgroundCollection, DataProvenance.Demo, null, OperatingAudience.Visitor);
        Assert.Equal("synthetic-field", await collection.CollectAsync(request, _ => Task.FromResult("synthetic-field"), Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => collection.CollectAsync(request, async token =>
        {
            await using var stopDb = new AppDbContext(options);
            Assert.True(await new SyntheticDriverOperatingStore(stopDb, clock).StopAsync(new(Guid.NewGuid(), "provenance-uncertain", clock.GetUtcNow(), true), token));
            return "delayed-field";
        }, Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => collection.CollectAsync<string>(request,
            _ => throw new InvalidOperationException("Stopped collection must not fetch"), Ct));
    }

    [Fact]
    public async Task MigratedOperatingHistoryCannotBeDiscardedByRollback()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using var db = new AppDbContext(options);
        await db.Database.EnsureDeletedAsync(Ct);
        await db.Database.MigrateAsync(Ct);
        var clock = new OperatingClock();
        var controls = new SyntheticDriverOperatingStore(db, clock);
        var pilot = DriverOperatingPolicyTests.Pilot();
        await controls.InitializeAsync(pilot.Manifest, pilot.Approvals[0], Ct);
        await Assert.ThrowsAsync<NotSupportedException>(() => db.GetService<IMigrator>().MigrateAsync("20261009005518_FenceLegacyUploadedLapReaders", Ct));
        Assert.NotNull(await controls.ReadAsync(Ct));
    }

    [Fact]
    public async Task ScopeChangesRecoveryAndDuplicateInitializationCannotRefundSpentUnits()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        var clock = new OperatingClock();
        await using var db = new AppDbContext(options);
        var controls = new SyntheticDriverOperatingStore(db, clock);
        var pilot = DriverOperatingPolicyTests.Pilot();
        var manifest = pilot.Manifest with { Budget = pilot.Manifest.Budget! with { PublicationUnits = 1 } };
        var smoke = pilot.Approvals[0] with { At = clock.GetUtcNow() };
        await controls.InitializeAsync(manifest, smoke, Ct);
        await controls.OptInAsync(manifest.Allowlist[0], 1, Ct);
        var request = DriverOperatingPolicyTests.Request(manifest.Allowlist[0]);
        var lease = (await controls.ReserveAsync(request, Ct))!;
        Assert.Null(await controls.ReserveAsync(request with { Provenance = DataProvenance.Real }, Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => controls.InitializeAsync(manifest, smoke, Ct));
        var stop = new OperatingStop(Guid.NewGuid(), "accounting-loss", clock.GetUtcNow(), true);
        Assert.True(await controls.StopAsync(stop, Ct));
        Assert.False(await controls.CurrentAsync(lease, Ct));
        Assert.True(await controls.RecoverAsync(new(stop.Id, stop.Cause, "synthetic-recovery", true, true, true, true,
            new(DriverOperatingStage.Pilot, clock.GetUtcNow(), "synthetic-pilot", true, true, true)), Ct));
        Assert.Null(await controls.ReserveAsync(request, Ct));
        manifest = manifest with { ScopeVersion = 2 };
        Assert.True(await controls.ChangeScopeAsync(manifest, smoke with { ScopeVersion = 2 }, Ct));
        Assert.False(await controls.OptInAsync(manifest.Allowlist[0], 1, Ct));
        Assert.True(await controls.OptInAsync(manifest.Allowlist[0], 2, Ct));
        Assert.Null(await controls.ReserveAsync(request, Ct));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.NotNull(await controls.ReserveAsync(request, Ct));
        Assert.False(await controls.CurrentAsync(lease, Ct));
    }
}

internal sealed class OperatingClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan time) => _now += time;
}
