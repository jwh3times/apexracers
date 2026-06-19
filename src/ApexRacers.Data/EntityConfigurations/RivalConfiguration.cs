using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class RivalConfiguration : IEntityTypeConfiguration<Rival>
{
    public void Configure(EntityTypeBuilder<Rival> builder)
    {
        builder.HasKey(r => r.Id);

        // One rival entry per (user, rival) — makes AddAsync idempotent.
        builder.HasIndex(r => new { r.UserId, r.RivalCustId }).IsUnique();

        builder.Property(r => r.DisplayName).HasMaxLength(100);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
