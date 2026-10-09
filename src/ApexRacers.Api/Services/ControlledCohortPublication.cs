using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Services;

public sealed record ControlledCohortEvidence(Guid CopyId, long CopyVersion, Guid PurposeId, long Generation,
    long PurposeVersion, DateTimeOffset OriginalAcquiredAt, DateTimeOffset PurposeCreatedAt);
public sealed record ControlledCohortSource(SyntheticCohortSnapshot Snapshot, ImmutableArray<DriverScope> Scopes,
    ImmutableArray<CandidateVariant> ReviewedVariants, ControlledCohortEvidence? Evidence = null);
public interface IControlledCohortSource
{
    Task<ControlledCohortSource?> LoadAsync(CancellationToken ct = default);
}
public interface IControlledCohortCatalog
{
    Task<ControlledCandidateReview?> ReviewAsync(ControlledCohortSource source, CancellationToken ct = default);
}
public sealed record CohortPublicationRequest(string CatalogId, PublicationPurpose Purpose, int PageOffset = 0, int PageSize = 20);

/// <summary>Explicit synthetic host seam. No ordinary route or startup registers these controlled
/// adapters. Feature callers receive only the protected executor, never a released candidate DTO.</summary>
public sealed class ControlledCohortPublication(IControlledCohortSource source, IControlledCohortCatalog catalog,
    PublicationReleaseStore releases, IDriverEnforcementJournal journal, Guid incarnation,
    IDriverPublicationObserver? observer = null, IDriverOperatingControls? operating = null)
{
    public async Task<IActionResult> ReadAsync(CohortPublicationRequest request, Guid? actualRecipientUserId, CancellationToken ct = default)
    {
        if (request.CatalogId != WholeCohortCandidates.CatalogId || !Enum.IsDefined(request.Purpose)
            || journal is not IPublicationHistoryAuthority
            || request.PageOffset is not (0 or 1) || request.PageSize != 20 || incarnation == Guid.Empty
            || (request.Purpose == PublicationPurpose.Aggregate ? actualRecipientUserId is not null : actualRecipientUserId is null || actualRecipientUserId == Guid.Empty))
            return Unavailable();
        var loaded = await source.LoadAsync(ct);
        if (loaded is null || loaded.Evidence is null || loaded.Snapshot.Provenance != DataProvenance.Demo || loaded.Scopes.IsDefaultOrEmpty
            || loaded.Scopes.Any(s => s.Provenance != DataProvenance.Demo)
            || !loaded.Scopes.Select(s => s.CustomerId).Order().SequenceEqual(loaded.Snapshot.Members.Select(m => m.CustomerId).Distinct().Order())) return Unavailable();
        var ownerScope = request.Purpose == PublicationPurpose.Owner
            ? loaded.Scopes.SingleOrDefault(s => s.UserId == actualRecipientUserId) : null;
        if (request.Purpose == PublicationPurpose.Owner && ownerScope is null) return Unavailable();
        var variant = loaded.ReviewedVariants.SingleOrDefault(v =>
            v.Audience == (request.Purpose == PublicationPurpose.Aggregate ? CandidateAudience.Visitor : CandidateAudience.SignedInPreview)
            && v.OwnerCustomerId == ownerScope?.CustomerId);
        if (variant is null) return Unavailable();
        var reviewed = await catalog.ReviewAsync(loaded, ct);
        if (reviewed is null || !reviewed.Variants.SequenceEqual(loaded.ReviewedVariants)) return Unavailable();
        var candidate = WholeCohortCandidates.Prepare(request.CatalogId, WholeCohortCandidates.Scope, loaded.Snapshot, reviewed, variant);
        if (candidate is null) return Unavailable();
        var reviewFingerprint = reviewed.Fingerprint;
        var required = loaded.Snapshot.Members.Where(m => m.CustomerId != variant.OwnerCustomerId
            && variant.Audience != CandidateAudience.Visitor && m.SharingAudiences.Contains(variant.Audience))
            .ToDictionary(m => m.CustomerId, _ => DriverConsentScope.Sharing);
        if (ownerScope is not null) required[ownerScope.CustomerId] = DriverConsentScope.Personal;
        var dependencies = await releases.ObserveAsync(loaded.Scopes, required, journal, ct);
        if (dependencies is null) return Unavailable();
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            HiddenGroups = WholeCohortCandidates.PageHidden(candidate, request.PageOffset, request.PageSize),
            candidate.Projection.NamedRows,
            candidate.Projection.OwnerAnalytics
        }, JsonSerializerOptions.Web);
        if (body.Length > 262144) return Unavailable();
        var sourceHash = Hash(loaded);
        var proposal = new PublicationProposal(Guid.NewGuid(), Hash(new { request, actualRecipientUserId, sourceHash, reviewFingerprint }),
            Convert.ToHexString(SHA256.HashData(body)), Hash(new { sourceHash, reviewFingerprint, dependencies }),
            request.CatalogId, loaded.Snapshot.CatalogRevision, DataProvenance.Demo, actualRecipientUserId,
            request.Purpose, incarnation, dependencies.Value);
        return new ProtectedCohortResult(proposal, body, sourceHash, reviewFingerprint, Hash(candidate.Projection), variant, request,
            source, catalog, releases, journal, observer, operating ?? new UnavailableDriverOperatingControls());
    }

    internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    internal static ObjectResult Unavailable() => new UnavailableCohortResult();
    private sealed class UnavailableCohortResult() : ObjectResult(new ProblemDetails { Status = 503, Detail = "Driver publication is unavailable." })
    {
        public override Task ExecuteResultAsync(ActionContext context)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            StatusCode = 503;
            return base.ExecuteResultAsync(context);
        }
    }
}

internal sealed class ProtectedCohortResult(PublicationProposal proposal, byte[] body, string sourceHash,
    string reviewFingerprint, string projectionHash, CandidateVariant variant, CohortPublicationRequest request,
    IControlledCohortSource source, IControlledCohortCatalog catalog, PublicationReleaseStore releases,
    IDriverEnforcementJournal journal, IDriverPublicationObserver? observer, IDriverOperatingControls operating) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;
        var ct = http.RequestAborted;
        http.Response.Headers.CacheControl = "no-store";
        var operatingLease = await DriverOperatingAdmission.ReserveAsync(http, operating, proposal.CatalogId, WholeCohortCandidates.Scope,
            proposal.Provenance, proposal.RecipientUserId, proposal.CatalogRevision, ct);
        if (operatingLease is null) { http.Response.StatusCode = 503; return; }
        async Task<bool> Current(CancellationToken token)
        {
            if (!await operating.CurrentAsync(operatingLease, token)) return false;
            var current = await source.LoadAsync(token);
            if (current is null || ControlledCohortPublication.Hash(current) != sourceHash) return false;
            var currentReview = await catalog.ReviewAsync(current, token);
            if (currentReview?.Fingerprint != reviewFingerprint) return false;
            var candidate = WholeCohortCandidates.Prepare(request.CatalogId, WholeCohortCandidates.Scope, current.Snapshot, currentReview, variant);
            return candidate is not null && ControlledCohortPublication.Hash(candidate.Projection) == projectionHash
                && await releases.NamesMatchAsync(current.Snapshot.Members.Where(m => m.CustomerId != variant.OwnerCustomerId
                    && variant.Audience != CandidateAudience.Visitor && m.SharingAudiences.Contains(variant.Audience))
                    .ToDictionary(m => m.CustomerId, m => m.DriverName!), token);
        }
        await Phase("before-admission", Guid.Empty, ct);
        var admitted = await releases.ReserveAsync(proposal, Current, journal, ct);
        if (admitted is null) { http.Response.StatusCode = 503; return; }
        var confirmedNeverStarted = false;
        try
        {
            await Phase("admitted", proposal.Id, ct);
            if (!await releases.BeginDispatchAsync(admitted, Current, journal, ct))
            {
                confirmedNeverStarted = true;
                http.Response.StatusCode = 503;
                return;
            }
            await Phase("dispatch-started", proposal.Id, ct);
            if (!await operating.CurrentAsync(operatingLease, ct)) { http.Response.StatusCode = 503; return; }
            http.Response.ContentType = "application/json";
            await http.Response.Body.WriteAsync(body, ct);
            await http.Response.Body.FlushAsync(ct);
            await Phase("first-written", proposal.Id, ct);
            await http.Response.CompleteAsync();
        }
        catch { http.Abort(); throw; }
        finally
        {
            using var terminal = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await Phase("transport-ended", proposal.Id, terminal.Token);
            await releases.CheckpointAsync(admitted, confirmedNeverStarted, terminal.Token);
            await Phase("checkpointed", proposal.Id, terminal.Token);
        }
    }
    private Task Phase(string phase, Guid id, CancellationToken ct) => observer?.PhaseAsync(phase, id, ct) ?? Task.CompletedTask;
}
