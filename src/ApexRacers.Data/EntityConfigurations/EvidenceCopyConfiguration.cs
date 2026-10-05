using ApexRacers.Core;
using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class EvidencePurposeConfiguration : IEntityTypeConfiguration<EvidencePurpose>
{
    public void Configure(EntityTypeBuilder<EvidencePurpose> b)
    {
        b.HasKey(p => p.Id);
        b.HasIndex(p => new { p.Provenance, p.Kind, p.SeasonId });
        b.Property(p => p.Generation).IsConcurrencyToken();
    }
}

public sealed class EvidenceCopyConfiguration : IEntityTypeConfiguration<EvidenceCopyMarker>
{
    public void Configure(EntityTypeBuilder<EvidenceCopyMarker> b)
    {
        b.HasKey(c => c.Id);
        b.Property(c => c.KeyHash).HasMaxLength(64).IsRequired();
        b.HasIndex(c => new { c.PurposeId, c.Kind, c.KeyHash, c.Version }).IsUnique();
        b.HasIndex(c => new { c.VerifiedRemovedAt, c.RemovalDueAt });
        b.HasOne<EvidencePurpose>().WithMany().HasForeignKey(c => c.PurposeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public static class ManagedEvidenceConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        foreach (var type in new[] { typeof(ExternalDataCache), typeof(Subsession), typeof(SubsessionResult),
                     typeof(SeasonCarBop), typeof(CarPercentileResult), typeof(Rival), typeof(AuthorizedDriverNameCopy) })
            model.Entity(type).HasOne(typeof(EvidenceCopyMarker)).WithMany()
                .HasForeignKey(nameof(IManagedEvidence.EvidenceCopyId)).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Week>().HasOne<EvidenceCopyMarker>().WithMany().HasForeignKey(w => w.WeatherEvidenceCopyId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Week>().HasOne<EvidenceCopyMarker>().WithMany().HasForeignKey(w => w.DemoWeatherEvidenceCopyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EvidenceDependencyConfiguration : IEntityTypeConfiguration<EvidenceCopyDependency>
{
    public void Configure(EntityTypeBuilder<EvidenceCopyDependency> b)
    {
        b.HasKey(d => new { d.CopyId, d.SourceCopyId });
        b.HasIndex(d => d.SourceCopyId);
        b.HasOne<EvidenceCopyMarker>().WithMany().HasForeignKey(d => d.CopyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EvidenceCopyMarker>().WithMany().HasForeignKey(d => d.SourceCopyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AuthorizedDriverNameConfiguration : IEntityTypeConfiguration<AuthorizedDriverNameCopy>
{
    public void Configure(EntityTypeBuilder<AuthorizedDriverNameCopy> b)
    {
        b.HasKey(c => c.Id);
        b.Property(c => c.DriverName).HasMaxLength(200).IsRequired();
        b.HasIndex(c => c.GrantId);
    }
}
