using ApexRacers.Data;
using Microsoft.AspNetCore.DataProtection;

namespace ApexRacers.Api.Services;

/// <summary>
/// Where the API keeps the Data Protection key ring that protects email-confirmation and
/// password-reset tokens (Identity's <c>DataProtectorTokenProvider</c>).
///
/// <para><b>Why it is persisted.</b> Left to its defaults, the key ring lives inside the container
/// and dies with the process. That was tolerable on an always-on host that rarely restarted. On a
/// host that scales to zero, the process that sent a link is routinely gone by the time the link is
/// followed, so every link would fail. The ring is therefore stored in the application database,
/// beside the Identity tables it protects, and moves with that database.</para>
///
/// <para><b>Keys are stored without an XML encryptor.</b> This was chosen deliberately (issue #527).
/// Anyone who can read this table can already read the accounts the tokens protect, and wrapping the
/// keys with a cloud key service would tie the database to it. Data Protection's warning that no
/// encryptor is configured is expected; like every <c>Microsoft.AspNetCore</c> category it is
/// already silenced by <c>EvidenceOperationalLogging</c>, so its absence from the logs proves
/// nothing either way.</para>
///
/// <para>The application name is fixed so that keys stay valid whatever the content root or host
/// the process happens to run under.</para>
/// </summary>
public static class DataProtectionRegistration
{
    /// <summary>The isolation name every API process shares. Changing it invalidates every outstanding link.</summary>
    public const string ApplicationName = "ApexRacers";

    public static IDataProtectionBuilder AddApexRacersDataProtection(this IServiceCollection services) =>
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<AppDbContext>();
}
