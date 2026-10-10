using System.Collections.Immutable;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Services;

public sealed record ControlledDriverDetail(DriverScope Scope, double OfficialBestLapSeconds, int IRating);
public sealed record ControlledDriverReferenceSource(ImmutableArray<ControlledDriverDetail> Drivers,
    ControlledCohortEvidence Evidence, long CatalogRevision = 1);
/// <summary>Explicit synthetic evidence/catalog adapters. Never registered by ordinary startup.</summary>
public interface IControlledDriverReferenceCatalog
{
    Task<ControlledDriverReferenceSource?> LoadAsync(CancellationToken ct = default);
    Task<string?> ReviewAsync(ControlledDriverReferenceSource source, CancellationToken ct = default);
}

/// <summary>Owns reference resolution and every protected byte. Normal startup supplies no synthetic
/// source or review; unavailable proof/journal/catalog never becomes permission through a request.</summary>
public sealed class ScopedDriverPublication(DriverReferenceStore references, IDriverEnforcementJournal journal,
    Guid incarnation, IControlledDriverReferenceCatalog? catalog = null, PublicationReleaseStore? releases = null,
    IDriverPublicationObserver? observer = null, IDriverOperatingControls? operating = null)
{
    public async Task<IActionResult> ReadOwnerAsync(Guid userId, CancellationToken ct = default)
    {
        if (catalog is null || releases is null) return Unavailable();
        var owner = await references.RecipientAsync(userId, ct);
        var source = await catalog.LoadAsync(ct);
        if (owner?.DriverName is null || !Valid(source)
            || await catalog.ReviewAsync(source!, ct) is not { } review) return Unavailable();
        var detail = source!.Drivers.SingleOrDefault(d => d.Scope == owner.Scope);
        if (detail is null) return Unavailable();
        return await PrepareAsync(userId, source, review, [],
            new ScopedDriverDetailDto(owner.DriverName, detail.OfficialBestLapSeconds, detail.IRating, "synthetic"), ct, owner: owner);
    }
    public async Task<IActionResult> DiscoverAsync(Guid recipientUserId, string? term, bool followedOnly = false,
        CancellationToken ct = default)
    {
        if (term?.Length > 100 || catalog is null || releases is null) return Unavailable();
        var recipient = await references.RecipientAsync(recipientUserId, ct);
        var source = await catalog.LoadAsync(ct);
        if (recipient is null || !Valid(source) || await catalog.ReviewAsync(source!, ct) is not { } review) return Unavailable();
        var followed = followedOnly ? await references.FollowedAsync(recipientUserId, source!.Drivers.Select(d => d.Scope).ToArray(), ct) : null;
        if (followedOnly) await (observer?.PhaseAsync("reference-follows-selected", Guid.Empty, ct) ?? Task.CompletedTask);
        var rows = new List<ScopedDriverDiscoveryDto>();
        var bindings = new List<DriverReferenceBinding>();
        foreach (var target in source!.Drivers.Where(d => d.Scope.UserId != recipientUserId))
        {
            var access = await references.TargetAsync(target.Scope, ct);
            if (access?.DriverName is not { } name || followed is not null && !followed.Contains(target.Scope)
                || term is { Length: > 0 } && !name.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            await (observer?.PhaseAsync("reference-candidate-selected", Guid.Empty, ct) ?? Task.CompletedTask);
            var detail = await references.IssueAsync(recipientUserId, target.Scope, DriverReferencePurpose.Detail, ct);
            var comparison = await references.IssueAsync(recipientUserId, target.Scope, DriverReferencePurpose.Comparison, ct);
            var follow = await references.IssueAsync(recipientUserId, target.Scope, DriverReferencePurpose.Follow, ct);
            if (detail is null || comparison is null || follow is null) return Unavailable();
            rows.Add(new(name, detail.Value, comparison.Value, follow.Value, detail.ExpiresAt, "synthetic"));
            foreach (var (token, purpose) in new[] { (detail.Value, DriverReferencePurpose.Detail), (comparison.Value, DriverReferencePurpose.Comparison), (follow.Value, DriverReferencePurpose.Follow) })
            {
                var binding = await references.ResolveAsync(recipientUserId, token, purpose, ct);
                if (binding is null || binding.Target != access || binding.Recipient != recipient) return Unavailable();
                bindings.Add(binding);
            }
        }
        if (rows.Count == 0) return Unavailable();
        return await PrepareAsync(recipientUserId, source, review, bindings, rows, ct, followedOnly: followedOnly);
    }
    public async Task<IActionResult> ReadAsync(Guid recipientUserId, string? reference, DriverReferencePurpose purpose,
        CancellationToken ct = default)
    {
        if (purpose is not (DriverReferencePurpose.Detail or DriverReferencePurpose.Comparison) || catalog is null || releases is null) return Unavailable();
        var binding = await references.ResolveAsync(recipientUserId, reference, purpose, ct);
        var source = await catalog.LoadAsync(ct);
        if (binding is null || !Valid(source) || await catalog.ReviewAsync(source!, ct) is not { } review) return Unavailable();
        var target = source!.Drivers.SingleOrDefault(d => d.Scope == binding.Target.Scope);
        var recipient = source.Drivers.SingleOrDefault(d => d.Scope == binding.Recipient.Scope);
        if (target is null || binding.Target.DriverName is null || purpose == DriverReferencePurpose.Comparison && recipient is null) return Unavailable();
        object dto = purpose == DriverReferencePurpose.Detail
            ? new ScopedDriverDetailDto(binding.Target.DriverName, target.OfficialBestLapSeconds, target.IRating, "synthetic")
            : new ScopedDriverComparisonDto(binding.Target.DriverName, recipient!.OfficialBestLapSeconds,
                target.OfficialBestLapSeconds, target.OfficialBestLapSeconds - recipient.OfficialBestLapSeconds, "synthetic");
        return await PrepareAsync(recipientUserId, source, review, [binding], dto, ct);
    }
    public async Task<IActionResult> FollowAsync(Guid recipientUserId, string? reference, CancellationToken ct = default)
    {
        if (catalog is null || releases is null || !DriverReferences.Valid(reference)) return Unavailable();
        var binding = await references.ResolveAsync(recipientUserId, reference, DriverReferencePurpose.Follow, ct);
        var source = await catalog.LoadAsync(ct);
        if (binding is null || !Valid(source) || await catalog.ReviewAsync(source!, ct) is not { } review
            || !source!.Drivers.Any(d => d.Scope == binding.Target.Scope)) return Unavailable();
        return await PrepareAsync(recipientUserId, source!, review, [binding], new ScopedDriverFollowDto(true, "synthetic"), ct, addFollow: true);
    }
    private async Task<IActionResult> PrepareAsync(Guid recipient, ControlledDriverReferenceSource source, string review,
        IReadOnlyList<DriverReferenceBinding> bindings, object dto, CancellationToken ct, bool followedOnly = false, bool addFollow = false, DriverAccess? owner = null)
    {
        var required = bindings.SelectMany(b => new[] { b.Recipient, b.Target }).Concat(owner is null ? [] : [owner]).DistinctBy(a => a.GrantId).ToArray();
        var dependencies = await releases!.ObserveAsync(required.Select(a => a.Scope).ToImmutableArray(),
            required.ToDictionary(a => a.Scope.CustomerId, a => a.Purpose), journal, ct);
        if (dependencies is null) return Unavailable();
        var body = JsonSerializer.SerializeToUtf8Bytes(dto, JsonSerializerOptions.Web);
        if (body.Length > 262144) return Unavailable();
        var sourceHash = ControlledCohortPublication.Hash(source);
        var proposal = new PublicationProposal(Guid.NewGuid(), ControlledCohortPublication.Hash(new { sourceHash, review, recipient, followedOnly, addFollow, owner, bindings = bindings.Select(b => b.Reference.TokenHash) }),
            ControlledCohortPublication.Hash(dto), ControlledCohortPublication.Hash(new { sourceHash, review, dependencies }),
            DriverReferences.CatalogId, source.CatalogRevision, DataProvenance.Demo, recipient, PublicationPurpose.SignedIn, incarnation, dependencies.Value);
        return new ProtectedScopedDriverResult(proposal, body, sourceHash, review, bindings, references, catalog!, releases, journal, observer, followedOnly, addFollow, owner, operating ?? new UnavailableDriverOperatingControls());
    }
    private static bool Valid(ControlledDriverReferenceSource? source) => source is { CatalogRevision: > 0 }
        && !source.Drivers.IsDefaultOrEmpty && source.Drivers.Length <= 12
        && source.Drivers.All(d => d.Scope.Provenance == DataProvenance.Demo && d.Scope.UserId != Guid.Empty
            && d.Scope.CustomerId > 0 && double.IsFinite(d.OfficialBestLapSeconds) && d.OfficialBestLapSeconds > 0 && d.IRating >= 0)
        && source.Drivers.Select(d => d.Scope.CustomerId).Distinct().Count() == source.Drivers.Length;
    internal static IActionResult Unavailable() => ControlledCohortPublication.Unavailable();
}

internal sealed class ProtectedScopedDriverResult(PublicationProposal proposal, byte[] body, string sourceHash, string reviewHash,
    IReadOnlyList<DriverReferenceBinding> bindings, DriverReferenceStore references, IControlledDriverReferenceCatalog catalog,
    PublicationReleaseStore releases, IDriverEnforcementJournal journal, IDriverPublicationObserver? observer, bool followedOnly, bool addFollow, DriverAccess? owner, IDriverOperatingControls operating) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;
        var ct = http.RequestAborted;
        http.Response.Headers.CacheControl = "no-store";
        var operatingLease = await DriverOperatingAdmission.ReserveAsync(http, operating, proposal.CatalogId, "official",
            proposal.Provenance, proposal.RecipientUserId, proposal.CatalogRevision, ct);
        if (operatingLease is null) { await UnavailableAsync(context); return; }
        async Task<bool> Current(CancellationToken token)
        {
            // Authentication is read again from the executing request, never an actor argument,
            // User-selected namespace, saved Follow or token possession.
            if (http.User.Identity?.IsAuthenticated != true || !Guid.TryParse(http.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var user)
                || user != proposal.RecipientUserId) return false;
            var source = await catalog.LoadAsync(token);
            if (source is null || ControlledCohortPublication.Hash(source) != sourceHash || await catalog.ReviewAsync(source, token) != reviewHash) return false;
            if (owner is not null && await references.RecipientAsync(user, token) != owner) return false;
            foreach (var binding in bindings)
            {
                // Resolve by hash without retaining the plaintext bearer in enforcement history.
                if (!await references.BindingCurrentAsync(user, binding, token)) return false;
            }
            return (!followedOnly || await references.FollowBindingsCurrentAsync(bindings, token))
                && await operating.CurrentAsync(operatingLease, token);
        }
        await Phase("before-admission", Guid.Empty, ct);
        PublicationAccounting? admission;
        try { admission = await releases.ReserveAsync(proposal, Current, journal, ct); }
        catch (EvidenceCopyUnavailableException)
        {
            // An uncertain independent append remains conservatively recorded. Generic denial
            // cannot classify it unsent or manufacture a missing primary admission checkpoint.
            await UnavailableAsync(context);
            return;
        }
        if (admission is null) { await UnavailableAsync(context); return; }
        var neverStarted = false;
        try
        {
            await Phase("admitted", proposal.Id, ct);
            if (!await releases.BeginDispatchAsync(admission, Current, journal, ct))
            { neverStarted = true; await UnavailableAsync(context); return; }
            if (addFollow && !await references.AddFollowAsync(proposal.RecipientUserId!.Value, bindings.Single(), ct))
            { await UnavailableAsync(context); return; }
            if (!await operating.CurrentAsync(operatingLease, ct)) { await UnavailableAsync(context); return; }
            // No awaiting externally controlled phase exists between the final recheck and write.
            http.Response.ContentType = "application/json";
            await http.Response.Body.WriteAsync(body, ct);
            await http.Response.Body.FlushAsync(ct);
            await Phase("first-written", proposal.Id, ct);
            await http.Response.CompleteAsync();
        }
        catch (EvidenceCopyUnavailableException) when (!http.Response.HasStarted)
        {
            await UnavailableAsync(context);
        }
        catch { http.Abort(); throw; }
        finally
        {
            using var terminal = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await Phase("transport-ended", proposal.Id, terminal.Token);
            await releases.CheckpointAsync(admission, neverStarted, terminal.Token);
            await Phase("checkpointed", proposal.Id, terminal.Token);
        }
    }
    private static async Task UnavailableAsync(ActionContext context)
    {
        await ScopedDriverPublication.Unavailable().ExecuteResultAsync(context);
        // A later durability failure must retain pending accounting without truncating the
        // generic denial or attempting a second response. The owned executor is now unsent
        // with respect to protected Driver bytes and cannot make any further transport writes.
        await context.HttpContext.Response.CompleteAsync();
    }
    private Task Phase(string phase, Guid id, CancellationToken ct) => observer?.PhaseAsync(phase, id, ct) ?? Task.CompletedTask;
}
