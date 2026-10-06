using ApexRacers.Core;
using ApexRacers.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, IRacingDataScope? dataScope = null)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    private readonly IRacingDataScope _dataScope = dataScope ?? new();
    public DataProvenance Provenance => _dataScope.Provenance;
    public DbSet<QuarantinedDataCache> QuarantinedDataCaches => Set<QuarantinedDataCache>();
    public DbSet<ProvenanceMigrationInventory> ProvenanceMigrationInventory => Set<ProvenanceMigrationInventory>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<SeasonCar> SeasonCars => Set<SeasonCar>();
    public DbSet<Week> Weeks => Set<Week>();
    public DbSet<Car> Cars => Set<Car>();
    public DbSet<Subsession> Subsessions => Set<Subsession>();
    public DbSet<SubsessionResult> SubsessionResults => Set<SubsessionResult>();
    public DbSet<UploadedLap> UploadedLaps => Set<UploadedLap>();
    public DbSet<PrivateUploadSession> PrivateUploadSessions => Set<PrivateUploadSession>();
    public DbSet<PrivateUploadedLap> PrivateUploadedLaps => Set<PrivateUploadedLap>();
    public DbSet<CarPercentileResult> CarPercentileResults => Set<CarPercentileResult>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<CarClass> CarClasses => Set<CarClass>();
    public DbSet<CarClassCar> CarClassCars => Set<CarClassCar>();
    public DbSet<SeasonCarClass> SeasonCarClasses => Set<SeasonCarClass>();
    public DbSet<SeasonCarBop> SeasonCarBops => Set<SeasonCarBop>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ExternalDataCache> ExternalDataCaches => Set<ExternalDataCache>();
    public DbSet<Rival> Rivals => Set<Rival>();
    public DbSet<SignInAddressFailure> SignInAddressFailures => Set<SignInAddressFailure>();

    public DbSet<KnownDevice> KnownDevices => Set<KnownDevice>();
    public DbSet<SignInAccountFailure> SignInAccountFailures => Set<SignInAccountFailure>();

    public DbSet<DriverAuthorizationGrant> DriverAuthorizationGrants => Set<DriverAuthorizationGrant>();
    public DbSet<DriverProofReceipt> DriverProofReceipts => Set<DriverProofReceipt>();
    public DbSet<DriverLifecycleOperation> DriverLifecycleOperations => Set<DriverLifecycleOperation>();
    public DbSet<DriverPublicationAdmission> DriverPublicationAdmissions => Set<DriverPublicationAdmission>();
    public DbSet<DriverTrackedCopy> DriverTrackedCopies => Set<DriverTrackedCopy>();
    public DbSet<DriverCopyCleanup> DriverCopyCleanups => Set<DriverCopyCleanup>();
    public DbSet<EvidencePurpose> EvidencePurposes => Set<EvidencePurpose>();
    public DbSet<EvidenceCopyMarker> EvidenceCopyMarkers => Set<EvidenceCopyMarker>();
    public DbSet<EvidenceCopyDependency> EvidenceCopyDependencies => Set<EvidenceCopyDependency>();
    public DbSet<AuthorizedDriverNameCopy> AuthorizedDriverNameCopies => Set<AuthorizedDriverNameCopy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // HasDefaultSchema applies to every entity in the model, including those registered
        // by base.OnModelCreating below. Every ASP.NET Identity entity type MUST therefore
        // have an explicit ToTable(..., "identity") override after the base call. If a future
        // Identity version introduces a new entity type, add a corresponding override here.
        modelBuilder.HasDefaultSchema("iracing");

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>().ToTable("Users", "identity");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("Roles", "identity");
        modelBuilder.Entity<IdentityUserRole<Guid>>(b =>
        {
            b.ToTable("UserRoles", "identity");
            // Enforce one role per user at the DB level. Compatible with the app's
            // Remove-then-Add role swaps (AuthService register, AdminService.SetUserRoleAsync,
            // and the ADMIN_SEED_EMAILS promotion in Program.cs); blocks any path — or manual
            // DB edit — that would give a user a second role.
            b.HasIndex(ur => ur.UserId).IsUnique();
        });
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", "identity");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", "identity");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", "identity");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", "identity");

        modelBuilder.Entity<FeatureFlag>()
            .HasIndex(f => f.Key)
            .IsUnique();

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens", "identity");
            b.HasIndex(t => t.TokenHash).IsUnique();
            b.HasIndex(t => t.UserId);
            b.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Sign-in throttling state (issue #300). Both live in the identity schema beside the users
        // they describe, and both cascade: a deleted account has no failures worth keeping.
        modelBuilder.Entity<SignInAddressFailure>(b =>
        {
            b.ToTable("SignInAddressFailures", "identity");
            // The lookup is always (account, address) and the pair must be unique — two rows for one
            // pair would split a counter and silently double the allowance.
            b.HasIndex(f => new { f.UserId, f.IpAddress }).IsUnique();
            // Purge sweeps by age alone, across every account.
            b.HasIndex(f => f.LastFailureAt);
            // 45 covers an IPv6 address; 11 more for a "%<scope-id>" suffix.
            b.Property(f => f.IpAddress).HasMaxLength(56).IsRequired();
            b.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(f => f.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Known devices (issue #314). Identity schema beside the users they describe, and cascading
        // for the same reason: a deleted account has no devices worth remembering.
        modelBuilder.Entity<KnownDevice>(b =>
        {
            b.ToTable("KnownDevices", "identity");
            // Recognition looks the device up by this alone, never by account — see
            // KnownDeviceStore's remarks on why keying it on the account would time an oracle.
            b.HasIndex(d => d.TokenHash).IsUnique();
            // The per-account cap reads this account's devices by last-seen order.
            b.HasIndex(d => new { d.UserId, d.LastSeenAt });
            // The purge sweeps by expiry alone, across every account.
            b.HasIndex(d => d.ExpiresAt);
            // SHA-256 as lowercase hex is always 64 characters.
            b.Property(d => d.TokenHash).HasMaxLength(64).IsRequired();
            b.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(d => d.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SignInAccountFailure>(b =>
        {
            b.ToTable("SignInAccountFailures", "identity");
            // One row per account, so the account is the key and the table cannot grow with traffic.
            b.HasKey(f => f.UserId);
            b.HasIndex(f => f.LastFailureAt);
            b.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(f => f.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        EntityConfigurations.ManagedEvidenceConfiguration.Configure(modelBuilder);
        modelBuilder.Entity<Subsession>().HasQueryFilter(s =>
            Provenance != DataProvenance.Unknown && s.Provenance == Provenance && EvidenceCopyMarkers.Any(c => c.Id == s.EvidenceCopyId
                && c.Provenance == Provenance && c.UnavailableAt == null && c.VerifiedRemovedAt == null
                && EvidencePurposes.Any(p => p.Id == c.PurposeId && p.Provenance == Provenance && p.Kind >= EvidencePurposeKind.SyntheticPreview && p.Kind <= EvidencePurposeKind.Sharing && p.Generation == c.Generation && p.OriginalEndedAt == null)));
        modelBuilder.Entity<SubsessionResult>().HasQueryFilter(r =>
            Provenance != DataProvenance.Unknown && r.Provenance == Provenance && EvidenceCopyMarkers.Any(c => c.Id == r.EvidenceCopyId
                && c.Provenance == Provenance && c.UnavailableAt == null && c.VerifiedRemovedAt == null
                && EvidencePurposes.Any(p => p.Id == c.PurposeId && p.Provenance == Provenance && p.Kind >= EvidencePurposeKind.SyntheticPreview && p.Kind <= EvidencePurposeKind.Sharing && p.Generation == c.Generation && p.OriginalEndedAt == null)));
        modelBuilder.Entity<CarPercentileResult>().HasQueryFilter(r =>
            Provenance != DataProvenance.Unknown && r.Provenance == Provenance && EvidenceCopyMarkers.Any(c => c.Id == r.EvidenceCopyId
                && c.Provenance == Provenance && c.UnavailableAt == null && c.VerifiedRemovedAt == null
                && EvidencePurposes.Any(p => p.Id == c.PurposeId && p.Provenance == Provenance && p.Kind >= EvidencePurposeKind.SyntheticPreview && p.Kind <= EvidencePurposeKind.Sharing && p.Generation == c.Generation && p.OriginalEndedAt == null)));
        modelBuilder.Entity<Rival>().HasQueryFilter(r =>
            Provenance != DataProvenance.Unknown && r.Provenance == Provenance && EvidenceCopyMarkers.Any(c => c.Id == r.EvidenceCopyId
                && c.Provenance == Provenance && c.UnavailableAt == null && c.VerifiedRemovedAt == null
                && EvidencePurposes.Any(p => p.Id == c.PurposeId && p.Provenance == Provenance && p.Kind >= EvidencePurposeKind.SyntheticPreview && p.Kind <= EvidencePurposeKind.Sharing && p.Generation == c.Generation && p.OriginalEndedAt == null)));
        modelBuilder.Entity<SeasonCarBop>().HasQueryFilter(r =>
            Provenance != DataProvenance.Unknown && r.Provenance == Provenance && EvidenceCopyMarkers.Any(c => c.Id == r.EvidenceCopyId
                && c.Provenance == Provenance && c.UnavailableAt == null && c.VerifiedRemovedAt == null
                && EvidencePurposes.Any(p => p.Id == c.PurposeId && p.Provenance == Provenance && p.Kind >= EvidencePurposeKind.SyntheticPreview && p.Kind <= EvidencePurposeKind.Sharing && p.Generation == c.Generation && p.OriginalEndedAt == null)));
        modelBuilder.Entity<ExternalDataCache>().HasQueryFilter(r =>
            Provenance != DataProvenance.Unknown && r.Provenance == Provenance && EvidenceCopyMarkers.Any(c => c.Id == r.EvidenceCopyId
                && c.Provenance == Provenance && c.UnavailableAt == null && c.VerifiedRemovedAt == null
                && EvidencePurposes.Any(p => p.Id == c.PurposeId && p.Provenance == Provenance && p.Kind >= EvidencePurposeKind.SyntheticPreview && p.Kind <= EvidencePurposeKind.Sharing && p.Generation == c.Generation && p.OriginalEndedAt == null)));
        modelBuilder.Entity<AuthorizedDriverNameCopy>().HasQueryFilter(r =>
            Provenance != DataProvenance.Unknown && r.Provenance == Provenance && EvidenceCopyMarkers.Any(c => c.Id == r.EvidenceCopyId
                && c.Provenance == Provenance && c.UnavailableAt == null && c.VerifiedRemovedAt == null
                && EvidencePurposes.Any(p => p.Id == c.PurposeId && p.Provenance == Provenance && p.Kind >= EvidencePurposeKind.SyntheticPreview && p.Kind <= EvidencePurposeKind.Sharing && p.Generation == c.Generation && p.OriginalEndedAt == null
                    && (p.Kind == EvidencePurposeKind.Personal || p.Kind == EvidencePurposeKind.Sharing)
                    && DriverAuthorizationGrants.Any(g => g.Id == r.GrantId && g.Id == p.GrantId && g.Revision == p.GrantRevision
                        && g.AuthorizedDriverName == r.DriverName && g.Provenance == Provenance && g.BindingActive && g.ProofValid
                        && g.PersonalConsentVersion != null && (p.Kind != EvidencePurposeKind.Sharing || g.SharingConsentVersion != null)))));
        // Uploaded evidence is user-supplied, not a synthetic acquisition adapter. It cannot
        // enter a Demo Field even when its recorder ID happens to equal a Demo Driver ID.
        modelBuilder.Entity<UploadedLap>().HasQueryFilter(_ => Provenance != DataProvenance.Demo);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareProvenanceWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareProvenanceWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareProvenanceWrites()
    {
        foreach (var week in ChangeTracker.Entries<Week>())
            if (week.Property(w => w.WeatherSummaryJson).IsModified
                || (week.State == EntityState.Added && week.Entity.WeatherSummaryJson is not null))
            {
                if (Provenance != DataProvenance.Real)
                    throw new InvalidOperationException("Real weather requires a real acquisition scope.");
                week.Entity.WeatherProvenance = DataProvenance.Real;
            }
        foreach (var entry in ChangeTracker.Entries<IProvenancedData>())
        {
            if (entry.State == EntityState.Added && entry.Entity.Provenance == DataProvenance.Unknown)
                entry.Entity.Provenance = Provenance;
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            if (entry.Entity.Provenance is not (DataProvenance.Real or DataProvenance.Demo))
                throw new InvalidOperationException("Unknown evidence provenance cannot be written.");
            if (entry.Entity is IManagedEvidence { EvidenceCopyId: null })
                throw new EvidenceCopyUnavailableException();
            if (entry.State == EntityState.Modified && entry.Property(nameof(IProvenancedData.Provenance)).IsModified)
                throw new InvalidOperationException("Stored evidence provenance cannot be reassigned.");
            if (entry.Entity is SubsessionResult { Provenance: DataProvenance.Real } result)
                result.DisplayName = null;
        }
    }
}
