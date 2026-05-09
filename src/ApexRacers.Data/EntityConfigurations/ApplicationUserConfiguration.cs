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

        // Conditional unique index: multiple users without an iRacing account must not conflict.
        builder.HasIndex(u => u.IRacingCustomerId)
            .IsUnique()
            .HasFilter("\"IRacingCustomerId\" IS NOT NULL");
    }
}
