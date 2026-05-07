using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class WeekConfiguration : IEntityTypeConfiguration<Week>
{
    public void Configure(EntityTypeBuilder<Week> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.TrackName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(w => w.CarClass)
            .IsRequired()
            .HasMaxLength(100);
    }
}
