using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public class ExternalDataCacheConfiguration : IEntityTypeConfiguration<ExternalDataCache>
{
    public void Configure(EntityTypeBuilder<ExternalDataCache> builder)
    {
        builder.HasKey(c => c.Id);

        builder.HasIndex(c => c.CacheKey).IsUnique();

        builder.Property(c => c.CacheKey).HasMaxLength(200);

        // Payload keeps the default string mapping (text on PostgreSQL) so arbitrarily
        // large serialized JSON fits and EF InMemory tests need no provider-specific
        // column type — we never query inside the payload, only round-trip it whole.
    }
}
