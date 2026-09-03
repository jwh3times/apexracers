using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class WeekConfiguration : IEntityTypeConfiguration<Week>
{
    public void Configure(EntityTypeBuilder<Week> builder)
    {
        builder.HasKey(w => w.Id);

        builder.HasIndex(w => new { w.SeasonId, w.RaceWeekIndex }).IsUnique();

        builder.HasOne(w => w.Track)
            .WithMany(t => t.Weeks)
            .HasForeignKey(w => w.TrackId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
