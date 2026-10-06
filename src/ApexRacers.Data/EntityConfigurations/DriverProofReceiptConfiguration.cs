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
        // Minimal enforcement bindings must survive physical account erasure. Current User
        // existence is checked when issuing a grant, never by retaining a profile forever.
        builder.HasIndex(p => p.UserId);
    }
}
