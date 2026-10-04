using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class DriverLifecycleOperationConfiguration : IEntityTypeConfiguration<DriverLifecycleOperation>
{
    public void Configure(EntityTypeBuilder<DriverLifecycleOperation> builder)
    {
        builder.ToTable("DriverLifecycleOperations", "iracing", table =>
        {
            table.HasCheckConstraint("CK_DriverOperation_Revision", "\"AppliedRevision\" > 0");
            table.HasCheckConstraint("CK_DriverOperation_Kind", "\"Kind\" IN (1, 2, 3, 4, 5)");
        });
        builder.HasKey(o => o.Id);
        builder.HasAlternateKey(o => new { o.Id, o.GrantId, o.OriginalLossAt });
        builder.HasIndex(o => new { o.GrantId, o.CompletedAt });
        builder.HasOne<DriverAuthorizationGrant>().WithMany().HasForeignKey(o => o.GrantId).OnDelete(DeleteBehavior.Restrict);
    }
}
