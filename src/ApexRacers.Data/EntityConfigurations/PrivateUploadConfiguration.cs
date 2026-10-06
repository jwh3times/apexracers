using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class PrivateUploadConfiguration : IEntityTypeConfiguration<PrivateUploadSession>
{
    public void Configure(EntityTypeBuilder<PrivateUploadSession> b)
    {
        b.HasKey(s => s.Id);
        b.HasIndex(s => new { s.Provenance, s.UserId, s.CustomerId, s.CarId, s.TrackId, s.RecordedAt }).IsUnique();
        b.HasIndex(s => s.EvidenceCopyId);
        b.HasOne<EvidenceCopyMarker>().WithMany().HasForeignKey(s => s.EvidenceCopyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Car>().WithMany().HasForeignKey(s => s.CarId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Track>().WithMany().HasForeignKey(s => s.TrackId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(s => s.Laps).WithOne().HasForeignKey(l => l.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PrivateUploadedLapConfiguration : IEntityTypeConfiguration<PrivateUploadedLap>
{
    public void Configure(EntityTypeBuilder<PrivateUploadedLap> b)
    {
        b.HasKey(l => l.Id);
        b.HasIndex(l => new { l.SessionId, l.LapNumber }).IsUnique();
        b.ToTable("PrivateUploadedLaps", "iracing", t =>
            t.HasCheckConstraint("CK_PrivateUploadedLap_Timed", "\"LapTimeSeconds\" > 0 AND \"LapTimeSeconds\" <= 1.7976931348623157E308"));
    }
}
