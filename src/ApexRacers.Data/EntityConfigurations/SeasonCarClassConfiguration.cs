using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class SeasonCarClassConfiguration : IEntityTypeConfiguration<SeasonCarClass>
{
    public void Configure(EntityTypeBuilder<SeasonCarClass> builder)
    {
        builder.HasKey(s => new { s.SeasonId, s.CarClassId });

        builder.HasOne(s => s.Season)
            .WithMany(s => s.SeasonCarClasses)
            .HasForeignKey(s => s.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
