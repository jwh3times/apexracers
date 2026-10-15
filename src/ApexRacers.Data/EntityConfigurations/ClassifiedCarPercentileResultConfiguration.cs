using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class ClassifiedCarPercentileResultConfiguration : IEntityTypeConfiguration<ClassifiedCarPercentileResult>
{
    public void Configure(EntityTypeBuilder<ClassifiedCarPercentileResult> b)
    {
        b.ToTable("SelectionScopedCarPercentileResults", t =>
        {
            t.HasCheckConstraint("CK_ClassifiedPercentile_Owner", "\"Provenance\" = 2 AND \"CustomerId\" > 0 AND ((\"OwnerKind\" = 0 AND \"CustomerId\" = 100001 AND \"GrantId\" IS NULL AND \"GrantRevision\" IS NULL AND \"PersonalBestLapEvidence\" = 1 AND \"UploadedSessionType\" IS NULL) OR (\"OwnerKind\" = 1 AND \"GrantId\" IS NOT NULL AND \"GrantRevision\" IS NOT NULL AND \"GrantRevision\" > 0))");
            t.HasCheckConstraint("CK_ClassifiedPercentile_Selection", "\"SelectionPolicyVersion\" = 1 AND (\"SelectionKey\" IN ('v1:0:0', 'v1:1:63') OR \"SelectionKey\" IN ('v1:2:1','v1:2:3','v1:2:5','v1:2:7','v1:2:9','v1:2:11','v1:2:13','v1:2:15','v1:2:17','v1:2:19','v1:2:21','v1:2:23','v1:2:25','v1:2:27','v1:2:29','v1:2:31','v1:2:33','v1:2:35','v1:2:37','v1:2:39','v1:2:41','v1:2:43','v1:2:45','v1:2:47','v1:2:49','v1:2:51','v1:2:53','v1:2:55','v1:2:57','v1:2:59','v1:2:61','v1:2:63'))");
            t.HasCheckConstraint("CK_ClassifiedPercentile_Reading", "\"PersonalBestLapSeconds\" > 0 AND \"PersonalBestLapSeconds\" <= 1.7976931348623157e308 AND \"PercentileRank\" > 0 AND \"PercentileRank\" < 100 AND \"TopSharePercent\" BETWEEN 1 AND 100 AND \"SampleSize\" > 0 AND \"ReadingRevision\" > 0 AND \"PreparationGeneration\" > 0 AND \"PreparationVersion\" >= 0 AND ((\"PersonalBestLapEvidence\" = 1 AND \"UploadedSessionType\" IS NULL) OR (\"PersonalBestLapEvidence\" = 2 AND \"UploadedSessionType\" IS NOT NULL AND \"UploadedSessionType\" BETWEEN 0 AND 5 AND \"SelectionKey\" <> 'v1:0:0'))");
        });
        b.HasKey(r => r.Id);
        b.Property(r => r.SelectionKey).HasMaxLength(32).IsRequired();
        b.Property(r => r.EvidenceCopyId).IsRequired();
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Car>().WithMany().HasForeignKey(r => r.CarId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Series>().WithMany().HasForeignKey(r => r.SeriesId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Week>().WithMany().HasForeignKey(r => r.WeekId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<DriverAuthorizationGrant>().WithMany()
            .HasForeignKey(r => new { r.GrantId, r.UserId, r.CustomerId, r.Provenance })
            .HasPrincipalKey(g => new { g.Id, g.UserId, g.CustomerId, g.Provenance }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EvidenceCopyMarker>().WithMany().HasForeignKey(r => r.EvidenceCopyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EvidencePurpose>().WithMany().HasForeignKey(r => r.PreparationPurposeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r => new { r.Provenance, r.UserId, r.OwnerKind, r.CustomerId, r.SelectionKey, r.SeriesId, r.CarId, r.WeekId }).IsUnique().HasDatabaseName("IX_ClassifiedPercentile_Identity");
    }
}
