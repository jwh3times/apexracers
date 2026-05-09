using Microsoft.AspNetCore.Identity;

namespace ApexRacers.Data;

public class ApplicationUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
    public long? IRacingCustomerId { get; set; }
}
