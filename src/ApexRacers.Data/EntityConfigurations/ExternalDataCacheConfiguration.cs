using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class ExternalDataCacheConfiguration : IEntityTypeConfiguration<ExternalDataCache>
{
    public void Configure(EntityTypeBuilder<ExternalDataCache> builder)
    {
        builder.HasKey(c => c.Id);
        // A new table fences binaries that still read/write the unqualified legacy table.
        builder.ToTable("MappedDataCaches", table => table.HasCheckConstraint(
            "CK_MappedDataCaches_KnownProvenance", "\"Provenance\" IN (1, 2)"));
        builder.HasIndex(c => new { c.Provenance, c.CacheKey }).IsUnique();

        builder.Property(c => c.CacheKey).HasMaxLength(ExternalDataCache.CacheKeyMaxLength);

        // Payload keeps the default string mapping (text on PostgreSQL) so arbitrarily
        // large serialized JSON fits and EF InMemory tests need no provider-specific
        // column type — we never query inside the payload, only round-trip it whole.
    }
}
