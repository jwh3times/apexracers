using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ApexRacers.Api.Services;

public class AuthService(UserManager<ApplicationUser> userManager, IConfiguration config, AppDbContext db)
{
    private const int AccessTokenMinutes = 15;
    private const int RefreshTokenDays   = 7;

    private static readonly string[] SelfAssignableRoles = ["Beta", "Alpha"];

    public async Task<AuthResultDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var user = new ApplicationUser
        {
            Id          = Guid.NewGuid(),
            UserName    = request.Email,
            Email       = request.Email,
            DisplayName = request.Email.Split('@')[0],
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            // Identity's error descriptions are user-facing by design (e.g. "Passwords must
            // have at least one digit."); surface them so the caller knows what to fix.
            throw new InvalidOperationException(
                string.Join(" ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, "Standard");

        var jwt     = await GenerateJwtAsync(user);
        var refresh = await CreateRefreshTokenAsync(user.Id, ct);
        return new AuthResultDto(jwt, user.Id, user.DisplayName, refresh);
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return LoginResult.Invalid;

        // A locked-out account is denied before the password is even checked, so a
        // correct guess during the lockout window still fails.
        if (await userManager.IsLockedOutAsync(user))
            return LoginResult.Locked;

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            // UserManager.CheckPasswordAsync does not track failures the way SignInManager
            // does, so increment the counter manually; it locks the account once the
            // configured MaxFailedAccessAttempts threshold is reached.
            await userManager.AccessFailedAsync(user);
            return await userManager.IsLockedOutAsync(user) ? LoginResult.Locked : LoginResult.Invalid;
        }

        await userManager.ResetAccessFailedCountAsync(user);

        var jwt     = await GenerateJwtAsync(user);
        var refresh = await CreateRefreshTokenAsync(user.Id, ct);
        return LoginResult.Success(new AuthResultDto(jwt, user.Id, user.DisplayName, refresh));
    }

    public async Task<AuthResultDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new InvalidOperationException("Display name cannot be empty.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        user.DisplayName = request.DisplayName.Trim();
        if (request.IRacingCustomerId.HasValue)
            user.IRacingCustomerId = request.IRacingCustomerId.Value;
        if (!string.IsNullOrWhiteSpace(request.ThemePreference) &&
            request.ThemePreference is "auto" or "light" or "dark")
            user.ThemePreference = request.ThemePreference;

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var newEmail = request.Email.Trim();
            if (!string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                var existing = await userManager.FindByEmailAsync(newEmail);
                if (existing is not null && existing.Id != userId)
                    throw new InvalidOperationException("Email address is already in use.");

                user.Email                = newEmail;
                user.NormalizedEmail      = userManager.NormalizeEmail(newEmail);
                user.UserName             = newEmail;
                user.NormalizedUserName   = userManager.NormalizeName(newEmail);
            }
        }

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException("Update failed. Please try again.");

        return new AuthResultDto(await GenerateJwtAsync(user), user.Id, user.DisplayName);
    }

    public async Task<AuthResultDto> UpdateRoleAsync(Guid userId, string newRole, CancellationToken ct = default)
    {
        if (!SelfAssignableRoles.Contains(newRole, StringComparer.OrdinalIgnoreCase) &&
            !string.Equals(newRole, "Standard", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Role must be Standard, Beta, or Alpha.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        var currentRoles = await userManager.GetRolesAsync(user);

        if (currentRoles.Contains("Admin"))
            throw new InvalidOperationException("Admin role cannot be changed via self-service.");

        await userManager.RemoveFromRolesAsync(user, currentRoles);
        await userManager.AddToRoleAsync(user, newRole);

        return new AuthResultDto(await GenerateJwtAsync(user), user.Id, user.DisplayName);
    }

    public async Task<AuthResultDto> UpdateThemeAsync(Guid userId, string themePreference, CancellationToken ct = default)
    {
        if (themePreference is not ("auto" or "light" or "dark"))
            throw new InvalidOperationException("Theme must be auto, light, or dark.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        user.ThemePreference = themePreference;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException("Update failed. Please try again.");

        return new AuthResultDto(await GenerateJwtAsync(user), user.Id, user.DisplayName);
    }

    public async Task<AuthResultDto> RefreshAsync(string rawToken, CancellationToken ct = default)
    {
        var hash   = HashToken(rawToken);
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsActive)
            throw new InvalidOperationException("Invalid or expired refresh token.");

        var user = await userManager.FindByIdAsync(stored.UserId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        // Rotate: revoke old, issue new in one SaveChanges
        stored.RevokedAt = DateTimeOffset.UtcNow;

        var rawNew = GenerateRawToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id        = Guid.NewGuid(),
            UserId    = stored.UserId,
            TokenHash = HashToken(rawNew),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(RefreshTokenDays),
        });
        await db.SaveChangesAsync(ct);

        var jwt = await GenerateJwtAsync(user);
        return new AuthResultDto(jwt, user.Id, user.DisplayName, rawNew);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct = default)
    {
        var hash   = HashToken(rawToken);
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Deletes refresh tokens whose expiry is older than <paramref name="retention"/>.
    /// Revoked and naturally-expired rows otherwise accumulate forever; this is invoked
    /// once at API startup. Uses a tracked delete so it works on every EF provider.
    /// </summary>
    public async Task<int> PurgeExpiredRefreshTokensAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff  = DateTimeOffset.UtcNow - retention;
        var expired = await db.RefreshTokens
            .Where(t => t.ExpiresAt < cutoff)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return 0;

        db.RefreshTokens.RemoveRange(expired);
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }

    // TODO: Validate state against a nonce store to prevent CSRF; exchange the authorization
    //       code for an iRacing access token via the Authorization Code flow; fetch driver
    //       profile (customerId, displayName) from iRacing; update ApplicationUser.IRacingCustomerId;
    //       re-issue JWT with updated claims
    public Task<AuthResultDto> HandleCallbackAsync(string code, string state, CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> CreateRefreshTokenAsync(Guid userId, CancellationToken ct)
    {
        var raw = GenerateRawToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id        = Guid.NewGuid(),
            UserId    = userId,
            TokenHash = HashToken(raw),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(RefreshTokenDays),
        });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    private static string GenerateRawToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashToken(string raw)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal async Task<string> GenerateJwtAsync(ApplicationUser user)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JWT_SIGNING_KEY"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var roles = await userManager.GetRolesAsync(user);
        var role  = roles.FirstOrDefault() ?? "Standard";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Name,  user.DisplayName),
            new("role", role),
        };
        if (user.IRacingCustomerId.HasValue)
            claims.Add(new Claim("iracing_id", user.IRacingCustomerId.Value.ToString()));
        claims.Add(new Claim("theme_preference", user.ThemePreference));

        var issuer   = config["JWT_ISSUER"]   ?? "ApexRacers.Api";
        var audience = config["JWT_AUDIENCE"] ?? "ApexRacers.Web";

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(AccessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Outcome of a login attempt. <see cref="Auth"/> is non-null only on success;
/// <see cref="LockedOut"/> distinguishes a throttled account from bad credentials.
/// </summary>
public record LoginResult(AuthResultDto? Auth, bool LockedOut)
{
    public static LoginResult Success(AuthResultDto auth) => new(auth, false);
    public static readonly LoginResult Invalid = new(null, false);
    public static readonly LoginResult Locked  = new(null, true);
}
