using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.HasIndex(s => new { s.SeriesId, s.Year, s.Quarter }).IsUnique();

        builder.HasMany(s => s.Weeks)
            .WithOne(w => w.Season)
            .HasForeignKey(w => w.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
