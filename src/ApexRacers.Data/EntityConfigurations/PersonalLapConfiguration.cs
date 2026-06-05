using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class PersonalLapConfiguration : IEntityTypeConfiguration<PersonalLap>
{
    public void Configure(EntityTypeBuilder<PersonalLap> builder)
    {
        builder.HasKey(p => p.Id);

        builder.HasIndex(p => new { p.UserId, p.CarId, p.TrackId });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Car)
            .WithMany(c => c.PersonalLaps)
            .HasForeignKey(p => p.CarId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Track)
            .WithMany(t => t.PersonalLaps)
            .HasForeignKey(p => p.TrackId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
