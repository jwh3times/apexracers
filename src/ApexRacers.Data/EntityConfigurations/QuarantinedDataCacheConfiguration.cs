using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class QuarantinedDataCacheConfiguration : IEntityTypeConfiguration<QuarantinedDataCache>
{
    public void Configure(EntityTypeBuilder<QuarantinedDataCache> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.CacheKey).IsUnique();
        builder.Property(c => c.CacheKey).HasMaxLength(ExternalDataCache.CacheKeyMaxLength);
    }
}
