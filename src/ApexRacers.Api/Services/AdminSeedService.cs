using ApexRacers.Data;
using Microsoft.AspNetCore.Identity;

namespace ApexRacers.Api.Services;

public class AdminSeedService(AppDbContext db, UserManager<ApplicationUser> userManager)
{
    public async Task PromoteConfirmedUsersAsync(string? configuredEmails, CancellationToken ct = default)
    {
        var emails = (configuredEmails ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var email in emails)
        {
            ct.ThrowIfCancellationRequested();
            var user = await userManager.FindByEmailAsync(email);
            // Email confirmation is required before granting privileges.
            if (user is null || !await userManager.IsEmailConfirmedAsync(user)) continue;

            var roles = await userManager.GetRolesAsync(user);
            if (roles.Contains("Admin")) continue;

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            EnsureSucceeded(await userManager.RemoveFromRolesAsync(user, roles));
            EnsureSucceeded(await userManager.AddToRoleAsync(user, "Admin"));
            await transaction.CommitAsync(ct);
        }
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Admin seeding failed: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
    }
}
