using System.Collections.Immutable;
using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Data;

/// <summary>Explicit controlled adapter, restricted to Demo manifests. Normal startup never
/// registers it; even direct construction cannot admit Real processing. PostgreSQL serializes
/// all hosts against one shared budget, stage and safety state. No admission grants Driver access.</summary>
public sealed class SyntheticDriverOperatingStore(AppDbContext db, TimeProvider clock) : IDriverOperatingControls
{
    public async Task InitializeAsync(DriverOperatingManifest manifest, OperatingApproval smoke, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var state = new DriverOperatingState(manifest, DriverOperatingStage.Smoke, smoke.At, smoke.At, 1, [], [smoke], [], null);
        if (smoke.Stage != DriverOperatingStage.Smoke || !DriverOperatingPolicy.Valid(state, now))
            throw new EvidenceCopyUnavailableException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        if (await db.Set<DriverOperatingRecord>().AnyAsync(ct)) throw new EvidenceCopyUnavailableException();
        var row = new DriverOperatingRecord { Snapshot = JsonSerializer.Serialize(state), WindowStartedAt = now };
        db.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        finally { db.Entry(row).State = EntityState.Detached; }
    }

    public Task<bool> OptInAsync(Guid user, long scopeVersion, CancellationToken ct = default) => ChangeAsync(state =>
        user == Guid.Empty || scopeVersion != state.Manifest.ScopeVersion ? null : state with
        { OptIns = state.OptIns.Where(o => o.UserId != user).Append(new(user, scopeVersion)).ToImmutableArray(), Revision = state.Revision + 1 }, ct);

    public Task<bool> PromoteAsync(OperatingApproval approval, CancellationToken ct = default) =>
        ChangeAsync(state => DriverOperatingPolicy.Promote(state, approval, clock.GetUtcNow()), ct);

    public Task<bool> StopAsync(OperatingStop stop, CancellationToken ct = default) =>
        ChangeAsync(state => DriverOperatingPolicy.Stop(state, stop, clock.GetUtcNow()), ct);

    public Task<bool> RecoverAsync(OperatingRecovery recovery, CancellationToken ct = default) =>
        ChangeAsync(state => DriverOperatingPolicy.Recover(state, recovery, clock.GetUtcNow()), ct);

    public Task<bool> ChangeScopeAsync(DriverOperatingManifest manifest, OperatingApproval smoke, CancellationToken ct = default) =>
        ChangeAsync(state => DriverOperatingPolicy.ChangeScope(state, manifest, smoke, clock.GetUtcNow()), ct);

    public async Task<DriverOperatingState?> ReadAsync(CancellationToken ct = default)
    {
        var row = await db.Set<DriverOperatingRecord>().AsNoTracking().SingleOrDefaultAsync(ct);
        return row is null ? null : JsonSerializer.Deserialize<DriverOperatingState>(row.Snapshot);
    }

    public async Task<OperatingLease?> ReserveAsync(OperatingRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var row = await db.Set<DriverOperatingRecord>().AsNoTracking().SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var state = JsonSerializer.Deserialize<DriverOperatingState>(row.Snapshot)!;
        var now = clock.GetUtcNow();
        if (!DriverOperatingPolicy.Allows(state, request, now) || now < row.WindowStartedAt) return null;
        var budget = state.Manifest.Budget!;
        if (now - row.WindowStartedAt >= budget.Window)
        {
            row.WindowStartedAt = now;
            row.AcquisitionUsed = 0;
            row.PublicationUsed = 0;
            row.Reservations = "[]";
        }
        if (request.Work == OperatingWork.Publication)
        {
            if (row.PublicationUsed >= budget.PublicationUnits) return null;
            row.PublicationUsed++;
        }
        else
        {
            if (row.AcquisitionUsed >= budget.AcquisitionUnits) return null;
            row.AcquisitionUsed++;
        }
        var lease = new OperatingLease(Guid.NewGuid(), state.Revision, request);
        var leases = JsonSerializer.Deserialize<List<OperatingLease>>(row.Reservations)!;
        leases.Add(lease);
        row.Reservations = JsonSerializer.Serialize(leases);
        db.Update(row);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        finally { db.Entry(row).State = EntityState.Detached; }
        return lease;
    }

    public async Task<bool> CurrentAsync(OperatingLease lease, CancellationToken ct = default)
    {
        var row = await db.Set<DriverOperatingRecord>().AsNoTracking().SingleOrDefaultAsync(ct);
        if (row is null) return false;
        var state = JsonSerializer.Deserialize<DriverOperatingState>(row.Snapshot)!;
        var now = clock.GetUtcNow();
        return lease.Revision == state.Revision && DriverOperatingPolicy.Allows(state, lease.Request, now)
            && now >= row.WindowStartedAt && now - row.WindowStartedAt < state.Manifest.Budget!.Window
            && JsonSerializer.Deserialize<List<OperatingLease>>(row.Reservations)!.Contains(lease);
    }

    private async Task<bool> ChangeAsync(Func<DriverOperatingState, DriverOperatingState?> change, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var row = await db.Set<DriverOperatingRecord>().AsNoTracking().SingleOrDefaultAsync(ct);
        if (row is null) return false;
        var next = change(JsonSerializer.Deserialize<DriverOperatingState>(row.Snapshot)!);
        if (next is null || !DriverOperatingPolicy.Valid(next, clock.GetUtcNow())) return false;
        row.Snapshot = JsonSerializer.Serialize(next);
        // A state change invalidates prepared work without refunding possibly used units.
        row.Reservations = "[]";
        db.Update(row);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        finally { db.Entry(row).State = EntityState.Detached; }
        return true;
    }

    private async Task LockAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) throw new EvidenceCopyUnavailableException();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(379)", ct);
    }
}
