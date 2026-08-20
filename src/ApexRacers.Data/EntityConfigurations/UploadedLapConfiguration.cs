using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class UploadedLapConfiguration : IEntityTypeConfiguration<UploadedLap>
{
    public void Configure(EntityTypeBuilder<UploadedLap> builder)
    {
        builder.HasKey(p => p.Id);

        builder.HasIndex(p => new { p.UserId, p.CarId, p.TrackId });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Car)
            .WithMany(c => c.UploadedLaps)
            .HasForeignKey(p => p.CarId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Track)
            .WithMany(t => t.UploadedLaps)
            .HasForeignKey(p => p.TrackId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
