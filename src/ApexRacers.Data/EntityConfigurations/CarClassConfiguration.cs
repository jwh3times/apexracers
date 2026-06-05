using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class CarClassConfiguration : IEntityTypeConfiguration<CarClass>
{
    public void Configure(EntityTypeBuilder<CarClass> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.ShortName)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasMany(c => c.CarClassCars)
            .WithOne(cc => cc.CarClass)
            .HasForeignKey(cc => cc.CarClassId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.SeasonCarClasses)
            .WithOne(sc => sc.CarClass)
            .HasForeignKey(sc => sc.CarClassId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
