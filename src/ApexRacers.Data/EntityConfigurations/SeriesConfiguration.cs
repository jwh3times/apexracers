using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class SeriesConfiguration : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Category).HasMaxLength(50);

        builder.HasMany(s => s.Seasons)
            .WithOne(s => s.Series)
            .HasForeignKey(s => s.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
