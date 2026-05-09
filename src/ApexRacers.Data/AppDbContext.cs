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
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
