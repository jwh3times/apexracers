using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class DriverCopyCleanupConfiguration : IEntityTypeConfiguration<DriverCopyCleanup>
{
    public void Configure(EntityTypeBuilder<DriverCopyCleanup> builder)
    {
        builder.ToTable("DriverCopyCleanups", "iracing", table =>
        {
            table.HasCheckConstraint("CK_DriverCleanup_Deadline", "\"DueAt\" >= \"OriginalLossAt\"");
            table.HasCheckConstraint("CK_DriverCleanup_Purpose", "\"Purpose\" IN (1, 2)");
            table.HasCheckConstraint("CK_DriverCleanup_ThroughRevision", "\"ThroughRevision\" > 0");
        });
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.OperationId, c.Purpose }).IsUnique();
        builder.HasIndex(c => new { c.VerifiedRemovedAt, c.DueAt });
        builder.HasOne<DriverLifecycleOperation>().WithMany()
            .HasForeignKey(c => new { c.OperationId, c.GrantId, c.OriginalLossAt })
            .HasPrincipalKey(o => new { o.Id, o.GrantId, o.OriginalLossAt }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DriverAuthorizationGrant>().WithMany().HasForeignKey(c => c.GrantId).OnDelete(DeleteBehavior.Restrict);
    }
}
