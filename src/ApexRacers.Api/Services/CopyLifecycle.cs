using ApexRacers.Core;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

/// <summary>Tracked copies recheck journal and current primary generation at persistence.</summary>
public sealed class CopyLifecycle(DriverAuthorization authority, DriverAuthorityStore store, IDriverEnforcementJournal journal)
{
    public async Task<Guid> CommitAsync(DriverAccess access, string payload, CancellationToken ct = default)
    {
        var current = await authority.ResolveAsync(access.Scope, access.Purpose, ct);
        var enforcement = await journal.ReadAsync(access.Scope, ct);
        if (current is null || current.GrantId != access.GrantId || current.Revision != access.Revision
            || !enforcement.Allows(access.Revision, access.Purpose))
            throw new InvalidOperationException("Driver authorization is unavailable.");
        return await store.CommitCopyAsync(access, payload, journal, ct);
    }

    public Task<int> ExecuteDueAsync(CancellationToken ct = default) => store.RemoveDueCopiesAsync(ct);
}
