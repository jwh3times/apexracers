using System.Text;
using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Services;

public interface IDriverPublicationObserver
{
    Task PhaseAsync(string phase, Guid admissionId, CancellationToken ct = default);
}

/// <summary>Returns a result whose executor owns all protected serialization and transport writes.
/// This initial artifact is explicitly synthetic; it admits no Real query catalog or projection.</summary>
public sealed class DriverPublication(
    DriverAuthorization authority, DriverAuthorityStore store, IDriverEnforcementJournal journal,
    Guid incarnation, IDriverPublicationObserver? observer = null)
{
    public async Task<IActionResult> ReadSyntheticOwnerAsync(DriverScope scope, CancellationToken ct = default)
    {
        if (scope.Provenance != DataProvenance.Demo) return Unavailable();
        var access = await authority.ResolveAsync(scope, DriverConsentScope.Personal, ct);
        return access is null ? Unavailable() : new ProtectedDriverResult(access, store, journal, incarnation, observer);
    }

    public async Task<IActionResult> ReadSyntheticSharingAsync(
        DriverScope scope, Guid recipientUserId, CancellationToken ct = default)
    {
        if (scope.Provenance != DataProvenance.Demo || recipientUserId == Guid.Empty || recipientUserId == scope.UserId)
            return Unavailable();
        var access = await authority.ResolveAsync(scope, DriverConsentScope.Sharing, ct);
        return access is null ? Unavailable() : new ProtectedDriverResult(access, store, journal, incarnation, observer);
    }

    private static ObjectResult Unavailable() => new UnavailableDriverResult();

    private sealed class UnavailableDriverResult() : ObjectResult(new ProblemDetails
    {
        Status = StatusCodes.Status503ServiceUnavailable,
        Detail = "Driver publication is unavailable.",
    })
    {
        public override Task ExecuteResultAsync(ActionContext context)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            StatusCode = StatusCodes.Status503ServiceUnavailable;
            return base.ExecuteResultAsync(context);
        }
    }
}

internal sealed class ProtectedDriverResult(
    DriverAccess access, DriverAuthorityStore store, IDriverEnforcementJournal journal,
    Guid incarnation, IDriverPublicationObserver? observer) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;
        var ct = http.RequestAborted;
        http.Response.Headers.CacheControl = "no-store";
        await PhaseAsync("before-admission", Guid.Empty, ct);
        var state = await journal.ReadAsync(access.Scope, ct);
        if (!state.Allows(access.Revision, access.Purpose)) { http.Response.StatusCode = 503; return; }
        var admission = await store.AdmitAsync(access, incarnation, journal, ct);
        if (admission is null) { http.Response.StatusCode = 503; return; }
        try
        {
            await PhaseAsync("admitted", admission.Id, ct);
            // Journal-first intent can appear before primary closure. Recheck at the final unsent
            // boundary; already admitted active transport remains part of the drain protocol.
            state = await journal.ReadAsync(access.Scope, ct);
            var current = await store.ResolveAsync(access.Scope, access.Purpose, ct);
            if (!state.Allows(access.Revision, access.Purpose) || current is null || current.Revision != access.Revision)
            { http.Response.StatusCode = 503; return; }
            http.Response.ContentType = "application/x-ndjson";
            var first = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new SyntheticDriverArtifact(access.DriverName,
                access.Purpose == DriverConsentScope.Personal ? "owner" : "signed-in-sharing", "synthetic"), JsonSerializerOptions.Web) + "\n");
            var last = "{\"complete\":true,\"provenance\":\"synthetic\"}\n"u8.ToArray();
            // Artifact is bounded before any output and contains no raw other-Driver identifiers,
            // uploads or private history. No arbitrary caller serializer/payload can bypass it.
            if (first.Length + last.Length > 4096) throw new InvalidOperationException("Protected output exceeds its bound.");
            await http.Response.Body.WriteAsync(first, ct);
            await http.Response.Body.FlushAsync(ct);
            await PhaseAsync("first-written", admission.Id, ct);
            await http.Response.Body.WriteAsync(last, ct);
            await http.Response.CompleteAsync();
        }
        catch
        {
            http.Abort();
            throw;
        }
        finally
        {
            // No detached writers exist. After this boundary the executor never writes again.
            // Request cancellation does not cancel durability work; a failed checkpoint leaves
            // the admission pending for ended-executor recovery, never a guessed completion.
            using var terminalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await PhaseAsync("transport-ended", admission.Id, terminalTimeout.Token);
            await store.CheckpointAsync(admission.Id, incarnation, terminalTimeout.Token);
            await PhaseAsync("checkpointed", admission.Id, terminalTimeout.Token);
        }
    }

    private Task PhaseAsync(string phase, Guid id, CancellationToken ct) => observer?.PhaseAsync(phase, id, ct) ?? Task.CompletedTask;
}
