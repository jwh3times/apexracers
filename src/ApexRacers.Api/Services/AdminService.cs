using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class AdminService(UserManager<ApplicationUser> userManager, AppDbContext db)
{
    private static readonly string[] ValidRoles = ["Standard", "Beta", "Alpha", "Admin"];

    public static readonly Dictionary<string, int> RoleHierarchy = new()
    {
        ["Standard"] = 0,
        ["Beta"] = 1,
        ["Alpha"] = 2,
        ["Admin"] = 3,
    };

    // ── Users ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AdminUserDto>> GetUsersAsync(CancellationToken ct = default)
    {
        var rows = await (
            from user in db.Users
            join ur in db.UserRoles on user.Id equals ur.UserId into userRoles
            from ur in userRoles.DefaultIfEmpty()
            join r in db.Roles on ur.RoleId equals r.Id into roles
            from r in roles.DefaultIfEmpty()
            select new { user.Id, user.Email, user.DisplayName, RoleName = r.Name }
        ).ToListAsync(ct);

        return rows.Select(r => new AdminUserDto(r.Id, r.Email!, r.DisplayName, r.RoleName ?? "Standard")).ToList();
    }

    public async Task<AdminUserDto> SetUserRoleAsync(Guid userId, string newRole, CancellationToken ct = default)
    {
        if (!ValidRoles.Contains(newRole, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Invalid role '{newRole}'.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        var currentRoles = await userManager.GetRolesAsync(user);

        // Admin membership is managed exclusively via ADMIN_SEED_EMAILS in Key Vault
        if (currentRoles.Contains("Admin"))
            throw new InvalidOperationException("Admin role is managed via Key Vault and cannot be changed here.");

        await userManager.RemoveFromRolesAsync(user, currentRoles);
        await userManager.AddToRoleAsync(user, newRole);

        return new AdminUserDto(user.Id, user.Email!, user.DisplayName, newRole);
    }

    // ── Feature flags ─────────────────────────────────────────────────────────

    public Task<List<FeatureFlagDto>> GetAllFlagsAsync(CancellationToken ct = default) =>
        db.FeatureFlags.OrderBy(f => f.Key).Select(f => ToDto(f)).ToListAsync(ct);

    public async Task<List<FeatureFlagDto>> GetFlagsForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        var roles = user is null ? [] : await userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? "Standard";
        return await GetFlagsForRoleAsync(role, ct);
    }

    public async Task<List<FeatureFlagDto>> GetFlagsForRoleAsync(string role, CancellationToken ct = default)
    {
        var userLevel = RoleHierarchy.GetValueOrDefault(role, 0);
        var flags = await db.FeatureFlags.ToListAsync(ct);
        return flags
            .Where(f => f.IsEnabled && RoleHierarchy.GetValueOrDefault(f.MinimumRole, 0) <= userLevel)
            .Select(ToDto)
            .ToList();
    }

    public async Task<FeatureFlagDto> CreateFlagAsync(CreateFeatureFlagRequest request, CancellationToken ct = default)
    {
        ValidateRole(request.MinimumRole);
        if (await db.FeatureFlags.AnyAsync(f => f.Key == request.Key, ct))
            throw new InvalidOperationException($"Flag key '{request.Key}' already exists.");

        var flag = new FeatureFlag
        {
            Key = request.Key,
            Name = request.Name,
            Description = request.Description,
            IsEnabled = request.IsEnabled,
            MinimumRole = request.MinimumRole,
        };
        db.FeatureFlags.Add(flag);
        await db.SaveChangesAsync(ct);
        return ToDto(flag);
    }

    public async Task<FeatureFlagDto> UpdateFlagAsync(int id, UpdateFeatureFlagRequest request, CancellationToken ct = default)
    {
        ValidateRole(request.MinimumRole);
        var flag = await db.FeatureFlags.FindAsync([id], ct)
            ?? throw new InvalidOperationException("Feature flag not found.");

        flag.Name = request.Name;
        flag.Description = request.Description;
        flag.IsEnabled = request.IsEnabled;
        flag.MinimumRole = request.MinimumRole;
        flag.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return ToDto(flag);
    }

    public async Task DeleteFlagAsync(int id, CancellationToken ct = default)
    {
        var flag = await db.FeatureFlags.FindAsync([id], ct)
            ?? throw new InvalidOperationException("Feature flag not found.");

        db.FeatureFlags.Remove(flag);
        await db.SaveChangesAsync(ct);
    }

    private static void ValidateRole(string role)
    {
        if (!ValidRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Invalid minimum role '{role}'.");
    }

    private static FeatureFlagDto ToDto(FeatureFlag f) =>
        new(f.Id, f.Key, f.Name, f.Description, f.IsEnabled, f.MinimumRole, f.CreatedAt, f.UpdatedAt);
}
