using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Series> Series => Set<Series>();
    public DbSet<Week> Weeks => Set<Week>();
    public DbSet<Car> Cars => Set<Car>();
    public DbSet<LapTimeEntry> LapTimeEntries => Set<LapTimeEntry>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<CarPercentileResult> CarPercentileResults => Set<CarPercentileResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
