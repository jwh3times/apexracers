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
    public DbSet<Subsession> Subsessions => Set<Subsession>();
    public DbSet<SubsessionResult> SubsessionResults => Set<SubsessionResult>();
    public DbSet<UploadedLap> UploadedLaps => Set<UploadedLap>();
    public DbSet<CarPercentileResult> CarPercentileResults => Set<CarPercentileResult>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<CarClass> CarClasses => Set<CarClass>();
    public DbSet<CarClassCar> CarClassCars => Set<CarClassCar>();
    public DbSet<SeasonCarClass> SeasonCarClasses => Set<SeasonCarClass>();
    public DbSet<SeasonCarBop> SeasonCarBops => Set<SeasonCarBop>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ExternalDataCache> ExternalDataCaches => Set<ExternalDataCache>();
    public DbSet<Rival> Rivals => Set<Rival>();
    public DbSet<SignInAddressFailure> SignInAddressFailures => Set<SignInAddressFailure>();
    public DbSet<SignInAccountFailure> SignInAccountFailures => Set<SignInAccountFailure>();

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
        modelBuilder.Entity<IdentityUserRole<Guid>>(b =>
        {
            b.ToTable("UserRoles", "identity");
            // Enforce one role per user at the DB level. Compatible with the app's
            // Remove-then-Add role swaps (AuthService register, AdminService.SetUserRoleAsync,
            // and the ADMIN_SEED_EMAILS promotion in Program.cs); blocks any path — or manual
            // DB edit — that would give a user a second role.
            b.HasIndex(ur => ur.UserId).IsUnique();
        });
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", "identity");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", "identity");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", "identity");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", "identity");

        modelBuilder.Entity<FeatureFlag>()
            .HasIndex(f => f.Key)
            .IsUnique();

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens", "identity");
            b.HasIndex(t => t.TokenHash).IsUnique();
            b.HasIndex(t => t.UserId);
            b.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Sign-in throttling state (issue #300). Both live in the identity schema beside the users
        // they describe, and both cascade: a deleted account has no failures worth keeping.
        modelBuilder.Entity<SignInAddressFailure>(b =>
        {
            b.ToTable("SignInAddressFailures", "identity");
            // The lookup is always (account, address) and the pair must be unique — two rows for one
            // pair would split a counter and silently double the allowance.
            b.HasIndex(f => new { f.UserId, f.IpAddress }).IsUnique();
            // Purge sweeps by age alone, across every account.
            b.HasIndex(f => f.LastFailureAt);
            // 45 covers an IPv6 address; 11 more for a "%<scope-id>" suffix.
            b.Property(f => f.IpAddress).HasMaxLength(56).IsRequired();
            b.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(f => f.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SignInAccountFailure>(b =>
        {
            b.ToTable("SignInAccountFailures", "identity");
            // One row per account, so the account is the key and the table cannot grow with traffic.
            b.HasKey(f => f.UserId);
            b.HasIndex(f => f.LastFailureAt);
            b.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(f => f.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
