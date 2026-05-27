using ApexRacers.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Series> Series => Set<Series>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<SeasonCar> SeasonCars => Set<SeasonCar>();
    public DbSet<Week> Weeks => Set<Week>();
    public DbSet<Car> Cars => Set<Car>();
    public DbSet<LapTimeEntry> LapTimeEntries => Set<LapTimeEntry>();
    public DbSet<PersonalLap> PersonalLaps => Set<PersonalLap>();
    public DbSet<CarPercentileResult> CarPercentileResults => Set<CarPercentileResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // HasDefaultSchema applies to every entity in the model, including those registered
        // by base.OnModelCreating below. Every ASP.NET Identity entity type MUST therefore
        // have an explicit ToTable(..., "identity") override after the base call. If a future
        // Identity version introduces a new entity type, add a corresponding override here.
        modelBuilder.HasDefaultSchema("iracing");

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>().ToTable("Users", "identity");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("Roles", "identity");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles", "identity");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", "identity");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", "identity");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", "identity");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", "identity");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
