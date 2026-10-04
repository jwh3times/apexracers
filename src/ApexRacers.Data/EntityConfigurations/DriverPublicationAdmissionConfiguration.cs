using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexRacers.Data.EntityConfigurations;

public sealed class DriverPublicationAdmissionConfiguration : IEntityTypeConfiguration<DriverPublicationAdmission>
{
    public void Configure(EntityTypeBuilder<DriverPublicationAdmission> builder)
    {
        builder.ToTable("DriverPublicationAdmissions", "iracing", table =>
        {
            table.HasCheckConstraint("CK_DriverAdmission_Revision", "\"Revision\" > 0");
            table.HasCheckConstraint("CK_DriverAdmission_Purpose", "\"Purpose\" IN (1, 2)");
        });
        builder.HasKey(a => a.Id);
        // TerminalAt alone determines drain completion. LeaseUntil is diagnostic and is
        // deliberately absent from this access path and from any terminality constraint.
        builder.HasIndex(a => new { a.GrantId, a.Purpose, a.TerminalAt });
        builder.HasIndex(a => a.Incarnation);
        builder.HasOne<DriverAuthorizationGrant>().WithMany().HasForeignKey(a => a.GrantId).OnDelete(DeleteBehavior.Restrict);
    }
}
