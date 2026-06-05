using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class CarClassCarConfiguration : IEntityTypeConfiguration<CarClassCar>
{
    public void Configure(EntityTypeBuilder<CarClassCar> builder)
    {
        builder.HasKey(c => new { c.CarClassId, c.CarId });
    }
}
