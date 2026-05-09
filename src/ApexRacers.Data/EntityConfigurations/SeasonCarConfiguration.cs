using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class SeasonCarConfiguration : IEntityTypeConfiguration<SeasonCar>
{
    public void Configure(EntityTypeBuilder<SeasonCar> builder)
    {
        builder.HasKey(sc => new { sc.SeasonId, sc.CarId });

        builder.HasOne(sc => sc.Season)
            .WithMany(s => s.SeasonCars)
            .HasForeignKey(sc => sc.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(sc => sc.Car)
            .WithMany(c => c.SeasonCars)
            .HasForeignKey(sc => sc.CarId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
