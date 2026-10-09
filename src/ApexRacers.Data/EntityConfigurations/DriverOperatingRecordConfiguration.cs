using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class DriverOperatingRecordConfiguration : IEntityTypeConfiguration<DriverOperatingRecord>
{
    public void Configure(EntityTypeBuilder<DriverOperatingRecord> builder)
    {
        builder.ToTable("SyntheticDriverOperatingControls", "iracing", table =>
        {
            table.HasCheckConstraint("CK_Operating_Singleton", "\"Id\" = 1");
            table.HasCheckConstraint("CK_Operating_Usage", "\"AcquisitionUsed\" >= 0 AND \"PublicationUsed\" >= 0");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Snapshot).IsRequired();
        builder.Property(r => r.Reservations).IsRequired();
    }
}
