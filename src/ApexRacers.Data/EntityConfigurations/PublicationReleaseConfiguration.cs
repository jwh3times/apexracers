using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class PublicationReleaseConfiguration : IEntityTypeConfiguration<PublicationRelease>
{
    public void Configure(EntityTypeBuilder<PublicationRelease> builder)
    {
        builder.ToTable("PublicationReleases", "iracing", table =>
        {
            table.HasCheckConstraint("CK_PublicationRelease_Revision", "\"Sequence\" > 0 AND \"CatalogRevision\" > 0");
            table.HasCheckConstraint("CK_PublicationRelease_Unsent", "NOT \"ProvenUnsent\" OR (\"TerminalAt\" IS NOT NULL AND \"DispatchStartedAt\" IS NULL)");
            table.HasCheckConstraint("CK_PublicationRelease_Purpose", "\"Purpose\" IN (1,2,3)");
            table.HasCheckConstraint("CK_PublicationRelease_Demo", "\"Provenance\" = 2");
            table.HasCheckConstraint("CK_PublicationRelease_Clocks", "(\"DispatchStartedAt\" IS NULL OR \"DispatchStartedAt\" >= \"ReservedAt\") AND (\"TerminalAt\" IS NULL OR \"TerminalAt\" >= \"ReservedAt\") AND (\"TerminalAt\" IS NULL OR \"DispatchStartedAt\" IS NULL OR \"TerminalAt\" >= \"DispatchStartedAt\")");
        });
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.Sequence).IsUnique();
        builder.HasIndex(r => new { r.TerminalAt, r.Incarnation });
        builder.Property(r => r.ContextHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.RepresentationHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.DependencyHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.CatalogId).HasMaxLength(100).IsRequired();
        builder.HasMany(r => r.Dependencies).WithOne().HasForeignKey(d => d.ReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PublicationReleaseDependencyConfiguration : IEntityTypeConfiguration<PublicationReleaseDependency>
{
    public void Configure(EntityTypeBuilder<PublicationReleaseDependency> builder)
    {
        builder.ToTable("PublicationReleaseDependencies", "iracing", table =>
        {
            table.HasCheckConstraint("CK_PublicationDependency_Revision", "(\"GrantId\" IS NULL AND \"Revision\" = 0 AND \"RequiredPurpose\" IS NULL) OR (\"GrantId\" IS NOT NULL AND \"Revision\" > 0)");
            table.HasCheckConstraint("CK_PublicationDependency_Purpose", "\"RequiredPurpose\" IS NULL OR \"RequiredPurpose\" IN (1,2)");
            table.HasCheckConstraint("CK_PublicationDependency_Demo", "\"Provenance\" = 2");
        });
        builder.HasKey(d => new { d.ReleaseId, d.UserId, d.CustomerId, d.Provenance });
        builder.HasIndex(d => new { d.UserId, d.CustomerId, d.Provenance });
        builder.Property(d => d.AuthorityHash).HasMaxLength(64).IsRequired();
        builder.HasOne<DriverAuthorizationGrant>().WithMany().HasForeignKey(d => d.GrantId).OnDelete(DeleteBehavior.Restrict);
    }
}
