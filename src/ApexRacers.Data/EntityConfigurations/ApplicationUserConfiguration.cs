using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.DisplayName)
            .IsRequired()
            .HasMaxLength(100);

        // This remains a legacy claim, never proof or consent. Fence old Identity writers
        // through a physical rename while preserving every existing unverified value.
        builder.Property(u => u.IRacingCustomerId).HasColumnName("ClaimedIRacingCustomerId");
        // Keep the constraint name used by conflict translation stable.
        builder.HasIndex(u => u.IRacingCustomerId)
            .IsUnique()
            .HasDatabaseName("IX_Users_IRacingCustomerId")
            .HasFilter("\"ClaimedIRacingCustomerId\" IS NOT NULL");
    }
}
