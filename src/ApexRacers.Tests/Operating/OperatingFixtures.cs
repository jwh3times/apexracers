using System.Collections.Immutable;
using ApexRacers.Core;
using ApexRacers.Data;
using Xunit;

namespace ApexRacers.Tests.Operating;

internal static class OperatingFixtures
{
    public static async Task SeedAsync(AppDbContext db, IEnumerable<OperatingScope> scopes, IEnumerable<Guid> users,
        CancellationToken ct, TimeProvider? clock = null, bool pilot = false)
    {
        clock ??= TimeProvider.System;
        var now = clock.GetUtcNow();
        var controls = new SyntheticDriverOperatingStore(db, clock);
        var members = users.ToImmutableArray();
        var start = pilot ? now : now.AddDays(-30);
        var manifest = new DriverOperatingManifest("controlled379-fixture", 1, DataProvenance.Demo, scopes.ToImmutableArray(), members,
            new(10000, 10000, TimeSpan.FromDays(365), "synthetic-work-units-not-provider-quota", DataProvenance.Demo, Verified: true));
        await controls.InitializeAsync(manifest, new(DriverOperatingStage.Smoke, start, "synthetic-smoke", true, true, true), ct);
        foreach (var user in members) Assert.True(await controls.OptInAsync(user, 1, ct));
        Assert.True(await controls.PromoteAsync(new(DriverOperatingStage.Pilot, start, "synthetic-pilot", true, true, true), ct));
        if (pilot) return;
        Assert.True(await controls.PromoteAsync(new(DriverOperatingStage.Alpha, start.AddDays(14), "synthetic-alpha", true, true, true), ct));
        Assert.True(await controls.PromoteAsync(new(DriverOperatingStage.Beta, start.AddDays(21), "synthetic-beta", true, true, true), ct));
        Assert.True(await controls.PromoteAsync(new(DriverOperatingStage.Standard, start.AddDays(28), "synthetic-standard", true, true, true, true), ct));
    }
}
