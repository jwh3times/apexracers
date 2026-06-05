using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class SubsessionResultConfiguration : IEntityTypeConfiguration<SubsessionResult>
{
    public void Configure(EntityTypeBuilder<SubsessionResult> builder)
    {
        builder.HasKey(r => new { r.SubsessionId, r.CustId });

        builder.HasIndex(r => new { r.CarId, r.SubsessionId });
        builder.HasIndex(r => r.CustId);

        builder.HasOne(r => r.Car)
            .WithMany()
            .HasForeignKey(r => r.CarId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.CarClass)
            .WithMany()
            .HasForeignKey(r => r.CarClassId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.ReasonOut).HasMaxLength(100);
    }
}
