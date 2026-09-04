using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class SubsessionConfiguration : IEntityTypeConfiguration<Subsession>
{
    public void Configure(EntityTypeBuilder<Subsession> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.HasIndex(s => new { s.SeasonId, s.RaceWeekIndex });
        builder.HasIndex(s => s.WeekId);
        builder.HasIndex(s => s.RaceSessionId);

        builder.HasOne(s => s.Season)
            .WithMany()
            .HasForeignKey(s => s.SeasonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Week)
            .WithMany(w => w.Subsessions)
            .HasForeignKey(s => s.WeekId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.Track)
            .WithMany()
            .HasForeignKey(s => s.TrackId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Results)
            .WithOne(r => r.Subsession)
            .HasForeignKey(r => r.SubsessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
