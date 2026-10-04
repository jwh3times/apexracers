using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class DriverTrackedCopyConfiguration : IEntityTypeConfiguration<DriverTrackedCopy>
{
    public void Configure(EntityTypeBuilder<DriverTrackedCopy> builder)
    {
        builder.ToTable("DriverTrackedCopies", "iracing", table =>
        {
            table.HasCheckConstraint("CK_DriverCopy_Revision", "\"Revision\" > 0");
            table.HasCheckConstraint("CK_DriverCopy_Purpose", "\"Purpose\" IN (1, 2)");
        });
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Payload).HasMaxLength(16 * 1024).IsRequired();
        builder.HasIndex(c => new { c.GrantId, c.Purpose, c.UnavailableAt });
        builder.HasOne<DriverAuthorizationGrant>().WithMany().HasForeignKey(c => c.GrantId).OnDelete(DeleteBehavior.Restrict);
    }
}
