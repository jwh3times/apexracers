namespace ApexRacers.Core.Models;

public sealed class PublicationRelease
{
    public Guid Id { get; set; }
    public long Sequence { get; set; }
    public string ContextHash { get; set; } = "";
    public string RepresentationHash { get; set; } = "";
    public string DependencyHash { get; set; } = "";
    public string CatalogId { get; set; } = "";
    public long CatalogRevision { get; set; }
    public DataProvenance Provenance { get; set; }
    public Guid? RecipientUserId { get; set; }
    public PublicationPurpose Purpose { get; set; }
    public Guid Incarnation { get; set; }
    public DateTimeOffset ReservedAt { get; set; }
    public DateTimeOffset? DispatchStartedAt { get; set; }
    public DateTimeOffset? TerminalAt { get; set; }
    public bool ProvenUnsent { get; set; }
    public ICollection<PublicationReleaseDependency> Dependencies { get; set; } = [];
}

public sealed class PublicationReleaseDependency
{
    public Guid ReleaseId { get; set; }
    public Guid UserId { get; set; }
    public int CustomerId { get; set; }
    public DataProvenance Provenance { get; set; }
    public Guid? GrantId { get; set; }
    public long Revision { get; set; }
    public DriverConsentScope? RequiredPurpose { get; set; }
    public string AuthorityHash { get; set; } = "";
}
