using System.Collections.Immutable;

namespace ApexRacers.Core;

public enum PublicationPurpose { Aggregate = 1, SignedIn = 2, Owner = 3 }
public sealed record PublicationDependency(DriverScope Scope, Guid? GrantId, long Revision, DriverConsentScope? RequiredPurpose, string AuthorityHash);
public sealed record PublicationProposal(Guid Id, string ContextHash, string RepresentationHash, string DependencyHash,
    string CatalogId, long CatalogRevision, DataProvenance Provenance, Guid? RecipientUserId, PublicationPurpose Purpose,
    Guid Incarnation, ImmutableArray<PublicationDependency> Dependencies);
public sealed record PublicationAccounting(long Sequence, PublicationProposal Proposal, DateTimeOffset ReservedAt,
    DateTimeOffset? DispatchStartedAt = null, DateTimeOffset? TerminalAt = null, bool ProvenUnsent = false)
{
    public bool DispatchStarted => DispatchStartedAt is not null;
    public bool Terminal => TerminalAt is not null;
}
public sealed record PublicationHistory(bool Available, ImmutableArray<PublicationAccounting> Releases, Guid Epoch = default);

/// <summary>Independently current accounting; unavailable production adapters never imply an empty history.</summary>
public interface IPublicationHistoryAuthority
{
    Task<PublicationHistory> ReadAsync(CancellationToken ct = default);
    Task<PublicationAccounting> ReserveAsync(PublicationProposal proposal, CancellationToken ct = default);
    Task MarkDispatchAsync(Guid releaseId, Guid incarnation, CancellationToken ct = default);
    Task CheckpointAsync(Guid releaseId, Guid incarnation, bool provenUnsent, CancellationToken ct = default);
}

public interface IPublicationCompositionReview
{
    Task<bool> AssessAsync(PublicationProposal proposal, ImmutableArray<PublicationAccounting> history, CancellationToken ct = default);
}

public sealed class UnavailablePublicationHistory : IPublicationHistoryAuthority
{
    public Task<PublicationHistory> ReadAsync(CancellationToken ct = default) => Task.FromResult(new PublicationHistory(false, []));
    public Task<PublicationAccounting> ReserveAsync(PublicationProposal proposal, CancellationToken ct = default) => Task.FromException<PublicationAccounting>(new EvidenceCopyUnavailableException());
    public Task MarkDispatchAsync(Guid releaseId, Guid incarnation, CancellationToken ct = default) => Task.FromException(new EvidenceCopyUnavailableException());
    public Task CheckpointAsync(Guid releaseId, Guid incarnation, bool provenUnsent, CancellationToken ct = default) => Task.FromException(new EvidenceCopyUnavailableException());
}

public sealed class UnavailablePublicationReview : IPublicationCompositionReview
{
    public Task<bool> AssessAsync(PublicationProposal proposal, ImmutableArray<PublicationAccounting> history, CancellationToken ct = default) => Task.FromResult(false);
}
