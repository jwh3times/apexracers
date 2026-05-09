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

        return new AuthResultDto(GenerateJwt(user), user.Id, user.DisplayName);
    }

    public async Task<AuthResultDto?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            return null;

        return new AuthResultDto(GenerateJwt(user), user.Id, user.DisplayName);
    }

    // TODO: Validate state against a nonce store to prevent CSRF; exchange the authorization
    //       code for an iRacing access token via the Authorization Code flow; fetch driver
    //       profile (customerId, displayName) from iRacing; update ApplicationUser.IRacingCustomerId;
    //       re-issue JWT with updated claims
    public Task<AuthResultDto> HandleCallbackAsync(string code, string state, CancellationToken ct = default)
        => throw new NotImplementedException();

    private string GenerateJwt(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JWT_SIGNING_KEY"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.Name, user.DisplayName),
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
