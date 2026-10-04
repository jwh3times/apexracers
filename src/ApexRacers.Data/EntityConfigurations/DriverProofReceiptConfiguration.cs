using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class DriverProofReceiptConfiguration : IEntityTypeConfiguration<DriverProofReceipt>
{
    public void Configure(EntityTypeBuilder<DriverProofReceipt> builder)
    {
        builder.ToTable("DriverProofReceipts", "iracing", table =>
            table.HasCheckConstraint("CK_DriverProof_Provenance", "\"Provenance\" IN (1, 2)"));
        builder.HasKey(p => p.Id);
        builder.HasAlternateKey(p => new { p.Id, p.UserId, p.CustomerId, p.Provenance });
        builder.Property(p => p.Authority).HasMaxLength(128).IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
