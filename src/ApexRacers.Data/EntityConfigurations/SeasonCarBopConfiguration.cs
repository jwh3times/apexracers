using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class SeasonCarBopConfiguration : IEntityTypeConfiguration<SeasonCarBop>
{
    public void Configure(EntityTypeBuilder<SeasonCarBop> builder)
    {
        builder.HasKey(b => new { b.SeasonId, b.WeekNumber, b.CarId });
        builder.HasIndex(b => new { b.SeasonId, b.WeekNumber });
    }
}
