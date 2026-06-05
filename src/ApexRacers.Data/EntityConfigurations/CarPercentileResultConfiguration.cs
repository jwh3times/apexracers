using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class CarPercentileResultConfiguration : IEntityTypeConfiguration<CarPercentileResult>
{
    public void Configure(EntityTypeBuilder<CarPercentileResult> builder)
    {
        builder.HasKey(r => r.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Car)
            .WithMany(c => c.CarPercentileResults)
            .HasForeignKey(r => r.CarId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Week)
            .WithMany(w => w.CarPercentileResults)
            .HasForeignKey(r => r.WeekId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Series>()
            .WithMany()
            .HasForeignKey(r => r.SeriesId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.UserId, r.CarId, r.SeriesId, r.WeekId }).IsUnique();
    }
}
