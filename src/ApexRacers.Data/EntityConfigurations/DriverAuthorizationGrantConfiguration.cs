using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class DriverAuthorizationGrantConfiguration : IEntityTypeConfiguration<DriverAuthorizationGrant>
{
    public void Configure(EntityTypeBuilder<DriverAuthorizationGrant> builder)
    {
        builder.ToTable("DriverAuthorizationGrants", "iracing", table =>
        {
            table.HasCheckConstraint("CK_DriverGrant_Provenance", "\"Provenance\" IN (1, 2)");
            table.HasCheckConstraint("CK_DriverGrant_Revision", "\"Revision\" > 0");
            table.HasCheckConstraint("CK_DriverGrant_ConsentProof", "\"ProofValid\" OR (\"PersonalConsentVersion\" IS NULL AND \"SharingConsentVersion\" IS NULL AND \"AuthorizedDriverName\" IS NULL)");
            table.HasCheckConstraint("CK_DriverGrant_SharingPersonal", "\"SharingConsentVersion\" IS NULL OR \"PersonalConsentVersion\" IS NOT NULL");
        });
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Revision).IsConcurrencyToken();
        builder.Property(g => g.PersonalConsentVersion).HasMaxLength(128);
        builder.Property(g => g.SharingConsentVersion).HasMaxLength(128);
        builder.Property(g => g.AuthorizedDriverName).HasMaxLength(200);
        builder.HasIndex(g => new { g.Provenance, g.UserId, g.CustomerId }).IsUnique();
        builder.HasIndex(g => new { g.Provenance, g.CustomerId }).IsUnique().HasFilter("\"BindingActive\" = TRUE");
        builder.HasIndex(g => new { g.Provenance, g.UserId }).IsUnique().HasFilter("\"BindingActive\" = TRUE");
        // The receipt must belong to this exact original User/Driver/namespace, rather than
        // merely being a valid receipt for some other association.
        builder.HasOne<DriverProofReceipt>().WithMany()
            .HasForeignKey(g => new { g.ProofReceiptId, g.UserId, g.CustomerId, g.Provenance })
            .HasPrincipalKey(p => new { p.Id, p.UserId, p.CustomerId, p.Provenance })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
