using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.ConfigName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Category).HasMaxLength(50);
        builder.Property(t => t.Location).HasMaxLength(200);
        builder.Property(t => t.TimeZone).HasMaxLength(100);
    }
}
