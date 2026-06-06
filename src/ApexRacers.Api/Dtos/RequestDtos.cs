namespace ApexRacers.Api.Dtos;

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
public record UpdateProfileRequest(string DisplayName, long? IRacingCustomerId = null, string? Email = null, string? ThemePreference = null);
public record UpdateRoleRequest(string Role);
public record UpdateThemeRequest(string ThemePreference);
public record AdminUpdateUserRoleRequest(string Role);
public record CreateFeatureFlagRequest(string Key, string Name, string? Description, bool IsEnabled, string MinimumRole);
public record UpdateFeatureFlagRequest(string Name, string? Description, bool IsEnabled, string MinimumRole);
public record RefreshRequest(string RefreshToken);
public record RevokeRequest(string RefreshToken);
