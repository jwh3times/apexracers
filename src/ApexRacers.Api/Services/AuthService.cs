using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace ApexRacers.Api.Services;

public class AuthService(UserManager<ApplicationUser> userManager, IConfiguration config)
{
    private static readonly string[] SelfAssignableRoles = ["Beta", "Alpha"];

    public async Task<AuthResultDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.Email.Split('@')[0],
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, "Standard");
        return new AuthResultDto(await GenerateJwtAsync(user), user.Id, user.DisplayName);
    }

    public async Task<AuthResultDto?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            return null;

        return new AuthResultDto(await GenerateJwtAsync(user), user.Id, user.DisplayName);
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

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var newEmail = request.Email.Trim();
            if (!string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                var existing = await userManager.FindByEmailAsync(newEmail);
                if (existing is not null && existing.Id != userId)
                    throw new InvalidOperationException("Email address is already in use.");

                user.Email = newEmail;
                user.NormalizedEmail = userManager.NormalizeEmail(newEmail);
                user.UserName = newEmail;
                user.NormalizedUserName = userManager.NormalizeName(newEmail);
            }
        }

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

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

        // Admins cannot self-demote via this endpoint
        if (currentRoles.Contains("Admin"))
            throw new InvalidOperationException("Admin role cannot be changed via self-service.");

        await userManager.RemoveFromRolesAsync(user, currentRoles);
        await userManager.AddToRoleAsync(user, newRole);

        return new AuthResultDto(await GenerateJwtAsync(user), user.Id, user.DisplayName);
    }

    // TODO: Validate state against a nonce store to prevent CSRF; exchange the authorization
    //       code for an iRacing access token via the Authorization Code flow; fetch driver
    //       profile (customerId, displayName) from iRacing; update ApplicationUser.IRacingCustomerId;
    //       re-issue JWT with updated claims
    public Task<AuthResultDto> HandleCallbackAsync(string code, string state, CancellationToken ct = default)
        => throw new NotImplementedException();

    internal async Task<string> GenerateJwtAsync(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JWT_SIGNING_KEY"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var roles = await userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? "Standard";

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
            new Claim("role", role),
        };
        if (user.IRacingCustomerId.HasValue)
            claims.Add(new Claim("iracing_id", user.IRacingCustomerId.Value.ToString()));

        var issuer = config["JWT_ISSUER"] ?? "ApexRacers.Api";
        var audience = config["JWT_AUDIENCE"] ?? "ApexRacers.Web";

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
