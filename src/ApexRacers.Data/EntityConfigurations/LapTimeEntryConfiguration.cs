using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class LapTimeEntryConfiguration : IEntityTypeConfiguration<LapTimeEntry>
{
    public void Configure(EntityTypeBuilder<LapTimeEntry> builder)
    {
        builder.HasKey(l => l.Id);

        builder.HasIndex(l => new { l.CarId, l.WeekId });
        builder.HasIndex(l => new { l.DriverCustomerId, l.WeekId });

        builder.HasOne(l => l.Car)
            .WithMany(c => c.LapTimeEntries)
            .HasForeignKey(l => l.CarId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Week)
            .WithMany(w => w.LapTimeEntries)
            .HasForeignKey(l => l.WeekId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
