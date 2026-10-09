using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Core;

namespace ApexRacers.Api.Services;

/// <summary>Only the authenticated executing principal supplies operating audience. Request
/// parameters, Driver claims and flags cannot enroll a User or impersonate an admitted recipient.</summary>
internal static class DriverOperatingAdmission
{
    public static OperatingRequest? Request(HttpContext http, string catalog, string collection,
        OperatingWork work, DataProvenance provenance, Guid? expectedUser = null)
    {
        Guid? actual = null;
        var audience = OperatingAudience.Visitor;
        if (http.User.Identity?.IsAuthenticated == true)
        {
            if (!Guid.TryParse(http.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var user) || user == Guid.Empty) return null;
            actual = user;
            audience = http.User.FindAll("role").Select(c => c.Value switch
            {
                "Admin" => OperatingAudience.Admin,
                "Alpha" => OperatingAudience.Alpha,
                "Beta" => OperatingAudience.Beta,
                _ => OperatingAudience.Standard,
            }).DefaultIfEmpty(OperatingAudience.Standard).Max();
        }
        if (expectedUser != actual) return null;
        return new(catalog, collection, work, provenance, actual, audience);
    }

    public static Task<OperatingLease?> ReserveAsync(HttpContext http, IDriverOperatingControls controls,
        string catalog, string collection, DataProvenance provenance, Guid? expectedUser, long catalogRevision, CancellationToken ct)
    {
        var request = Request(http, catalog, collection, OperatingWork.Publication, provenance, expectedUser);
        return request is null ? Task.FromResult<OperatingLease?>(null) : controls.ReserveAsync(request with { CatalogRevision = catalogRevision }, ct);
    }
}
