using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class ScopedDriverReferenceConfiguration : IEntityTypeConfiguration<ScopedDriverReference>
{
    public void Configure(EntityTypeBuilder<ScopedDriverReference> b)
    {
        b.ToTable("ScopedDriverReferences", "iracing", t =>
        {
            t.HasCheckConstraint("CK_DriverReference_Lifetime", "\"ExpiresAt\" > \"CreatedAt\"");
            t.HasCheckConstraint("CK_DriverReference_Purpose", "\"Purpose\" IN (1,2,3)");
            t.HasCheckConstraint("CK_DriverReference_Revision", "\"TargetRevision\" > 0 AND \"RecipientRevision\" > 0");
            t.HasCheckConstraint("CK_DriverReference_Demo", "\"Provenance\" = 2 AND \"TargetGrantId\" != \"RecipientGrantId\"");
        });
        b.HasKey(r => r.TokenHash);
        b.Property(r => r.TokenHash).HasMaxLength(64);
        b.HasIndex(r => r.ExpiresAt);
        b.HasOne<DriverAuthorizationGrant>().WithMany().HasForeignKey(r => r.RecipientGrantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DriverAuthorizationGrant>().WithMany().HasForeignKey(r => r.TargetGrantId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class PrivateDriverFollowConfiguration : IEntityTypeConfiguration<PrivateDriverFollow>
{
    public void Configure(EntityTypeBuilder<PrivateDriverFollow> b)
    {
        b.ToTable("PrivateDriverFollows", "iracing", t =>
        {
            t.HasCheckConstraint("CK_PrivateFollow_Demo", "\"Provenance\" = 2 AND \"TargetGrantId\" != \"RecipientGrantId\"");
            t.HasCheckConstraint("CK_PrivateFollow_Clocks", "(\"OriginalLossAt\" IS NULL AND \"ReactivateBefore\" IS NULL AND \"RemoveBy\" IS NULL AND \"Active\") OR (\"OriginalLossAt\" IS NOT NULL AND \"ReactivateBefore\" IS NOT NULL AND \"RemoveBy\" IS NOT NULL)");
        });
        b.HasKey(f => f.Id);
        b.HasIndex(f => new { f.RecipientGrantId, f.TargetGrantId }).IsUnique();
        b.HasIndex(f => f.RemoveBy);
        b.HasOne<DriverAuthorizationGrant>().WithMany().HasForeignKey(f => f.RecipientGrantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DriverAuthorizationGrant>().WithMany().HasForeignKey(f => f.TargetGrantId).OnDelete(DeleteBehavior.Restrict);
    }
}
