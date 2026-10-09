using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>Authenticated own-User withdrawal. Stored bindings choose the association;
/// requests cannot supply another User, Customer ID, proof or authorization.</summary>
public sealed class DriverPrivacy(AppDbContext db, DriverAuthorization authorization, IDriverEnforcementJournal journal)
{
    public async Task<IActionResult> WithdrawAsync(Guid userId, DriverWithdrawalRequest request, CancellationToken ct = default)
    {
        if (request.OperationId == Guid.Empty || request.Scope is not ("personal" or "sharing"))
            throw new ArgumentException("A valid withdrawal operation and scope are required.");
        var kind = request.Scope == "personal" ? DriverLifecycleKind.WithdrawPersonal : DriverLifecycleKind.WithdrawSharing;
        var old = await db.DriverLifecycleOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == request.OperationId, ct);
        var grant = old is not null
            ? await db.DriverAuthorizationGrants.AsNoTracking().SingleOrDefaultAsync(g => g.Id == old.GrantId && g.UserId == userId, ct)
            : await db.DriverAuthorizationGrants.AsNoTracking().SingleOrDefaultAsync(g => g.UserId == userId && g.BindingActive && g.Provenance == DataProvenance.Demo, ct);
        if (grant is null || grant.Provenance != DataProvenance.Demo || old is not null && old.Kind != kind)
            return Unavailable();
        var scope = new DriverScope(userId, grant.CustomerId, grant.Provenance);
        if (!(await journal.ReadCurrentAsync(scope, ct)).Available) return Unavailable();
        var outcome = await authorization.TransitionAsync(scope, kind, request.OperationId, ct);
        return new ObjectResult(new DriverWithdrawalDto(outcome.OperationId, true, outcome.Completed,
            outcome.OriginalLossAt, false, false))
        { StatusCode = outcome.Completed ? 200 : 202 };
    }
    private static IActionResult Unavailable() => new ObjectResult(new ProblemDetails
    { Status = 503, Detail = "Driver withdrawal is unavailable. Keep affected display closed and retry." })
    { StatusCode = 503 };
}
