using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using ApexRacers.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>Verified Personal history remains owned by a protected executor. Ordinary startup
/// supplies no positive proof, journal or operating adapter for this controlled synthetic path.</summary>
public sealed class PercentileHistoryPublication(AppDbContext db, DriverAuthorization authority,
    DriverAuthorityStore store, IDriverEnforcementJournal journal, PercentileRefresh history, Guid incarnation,
    IDriverPublicationObserver? observer = null, IDriverOperatingControls? operating = null)
{
    public async Task<IActionResult> ReadPersonalAsync(Guid authenticatedUserId, PercentileSelection selection,
        int? seriesId = null, CancellationToken ct = default)
    {
        if (db.Provenance != DataProvenance.Demo || authenticatedUserId == Guid.Empty) return Unavailable();
        var bindings = await db.Set<DriverAuthorizationGrant>().AsNoTracking()
            .Where(g => g.UserId == authenticatedUserId && g.Provenance == db.Provenance && g.BindingActive)
            .Select(g => new { g.CustomerId, g.Id, g.Revision }).Take(2).ToListAsync(ct);
        if (bindings.Count != 1) return Unavailable();
        var binding = bindings[0];
        var access = await authority.ResolveAsync(new(authenticatedUserId, binding.CustomerId, db.Provenance), DriverConsentScope.Personal, ct);
        if (access is null || access.GrantId != binding.Id || access.Revision != binding.Revision) return Unavailable();
        try
        {
            var snapshot = await history.ReadPersonalHistoryAsync(access, selection, seriesId, ct);
            return new ProtectedHistoryResult(access, snapshot, history, store, journal, incarnation, observer,
                operating ?? new UnavailableDriverOperatingControls());
        }
        catch (EvidenceCopyUnavailableException) { return Unavailable(); }
    }

    private static IActionResult Unavailable() => new HistoryUnavailableResult();

    private sealed class HistoryUnavailableResult() : ObjectResult(new ProblemDetails
    {
        Status = StatusCodes.Status503ServiceUnavailable,
        Detail = "Personal history is unavailable.",
    })
    {
        public override Task ExecuteResultAsync(ActionContext context)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            StatusCode = StatusCodes.Status503ServiceUnavailable;
            return base.ExecuteResultAsync(context);
        }
    }

    private sealed class ProtectedHistoryResult(DriverAccess access, PercentileHistorySnapshot snapshot,
        PercentileRefresh history, DriverAuthorityStore store, IDriverEnforcementJournal journal, Guid incarnation,
        IDriverPublicationObserver? observer, IDriverOperatingControls operating) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var http = context.HttpContext;
            var ct = http.RequestAborted;
            http.Response.Headers.CacheControl = "no-store";
            if (http.User.Identity?.IsAuthenticated != true
                || !Guid.TryParse(http.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
                || userId != access.Scope.UserId)
            { await Unavailable().ExecuteResultAsync(context); return; }
            // Projection and serialization are module-owned; a caller cannot provide a serializer,
            // other recipient or a payload containing internal source/grant identifiers.
            var payload = JsonSerializer.SerializeToUtf8Bytes(PercentileAnalyticsProjection.Build(snapshot), JsonSerializerOptions.Web);
            if (payload.Length > 262_144) throw new InvalidOperationException("Protected output exceeds its bound.");
            await PhaseAsync("before-admission", Guid.Empty, ct);
            var lease = await DriverOperatingAdmission.ReserveAsync(http, operating, "synthetic-percentile-history-v1", "personal",
                access.Scope.Provenance, access.Scope.UserId, 1, ct);
            if (lease is null) { await Unavailable().ExecuteResultAsync(context); return; }
            var admission = await store.AdmitAsync(access, incarnation, journal, ct);
            if (admission is null) { await Unavailable().ExecuteResultAsync(context); return; }
            try
            {
                await PhaseAsync("admitted", admission.Id, ct);
                // Keep the complete snapshot/source/authority validation as the final asynchronous
                // check before the first byte. Do not bless changed inputs with a fresh capture.
                if (!await operating.CurrentAsync(lease, ct) || !await history.SnapshotCurrentAsync(snapshot, ct))
                {
                    await Unavailable().ExecuteResultAsync(context);
                    await http.Response.CompleteAsync();
                    return;
                }
                http.Response.ContentType = "application/json";
                await http.Response.Body.WriteAsync(payload, ct);
                await http.Response.Body.FlushAsync(ct);
                await PhaseAsync("first-written", admission.Id, ct);
                await http.Response.CompleteAsync();
            }
            catch { http.Abort(); throw; }
            finally
            {
                // Once transport-ended is reached this executor never writes again. Cancellation
                // cannot turn an uncheckpointed writer into an assumed completed writer.
                using var terminalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await PhaseAsync("transport-ended", admission.Id, terminalTimeout.Token);
                await store.CheckpointAsync(admission.Id, incarnation, terminalTimeout.Token);
                await PhaseAsync("checkpointed", admission.Id, terminalTimeout.Token);
            }
        }

        private Task PhaseAsync(string phase, Guid id, CancellationToken ct) => observer?.PhaseAsync(phase, id, ct) ?? Task.CompletedTask;
    }
}
