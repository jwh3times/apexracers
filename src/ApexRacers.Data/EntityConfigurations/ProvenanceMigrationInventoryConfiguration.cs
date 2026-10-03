using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class ProvenanceMigrationInventoryConfiguration : IEntityTypeConfiguration<ProvenanceMigrationInventory>
{
    public void Configure(EntityTypeBuilder<ProvenanceMigrationInventory> builder)
    {
        builder.HasKey(i => i.StorageKind);
        builder.Property(i => i.StorageKind).HasMaxLength(80);
    }
}
