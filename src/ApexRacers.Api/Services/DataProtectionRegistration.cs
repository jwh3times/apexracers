using ApexRacers.Data;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace ApexRacers.Api.Services;

/// <summary>
/// Where the API keeps the Data Protection key ring that protects email-confirmation and
/// password-reset tokens (Identity's <c>DataProtectorTokenProvider</c>), and how it is protected.
///
/// <para><b>Why it is persisted.</b> Left to its defaults, the key ring lives inside the container
/// and dies with the process. That was tolerable on an always-on host that rarely restarted. On a
/// host that scales to zero, the process that sent a link is routinely gone by the time the link is
/// followed, so every link would fail. The ring is therefore stored in the application database,
/// beside the Identity tables it protects, and moves with that database.</para>
///
/// <para><b>Why a deployed host must encrypt it.</b> A read-only copy of the database (a backup, a
/// restore copy, a leaked dump) holds both the key ring and every User's <c>SecurityStamp</c>. In
/// the clear, that is enough to forge password-reset tokens and take over accounts, which turns read
/// access into account takeover. So wherever the API reads Key Vault
/// (<c>AZURE_KEY_VAULT_URL</c>), it also wraps the ring with a Key Vault key named by
/// <see cref="KeyIdVariable"/>, and startup fails without one rather than silently storing keys in the
/// clear (issue #527). Local Compose and CI read no Key Vault and keep the ring unencrypted.</para>
///
/// <para>The application name is fixed so that keys stay valid whatever the content root or host
/// the process happens to run under.</para>
/// </summary>
public static class DataProtectionRegistration
{
    /// <summary>The isolation name every API process shares. Changing it invalidates every outstanding link.</summary>
    public const string ApplicationName = "ApexRacers";

    /// <summary>
    /// The Key Vault key that wraps the key ring, as a key identifier
    /// (<c>https://&lt;vault&gt;.vault.azure.net/keys/&lt;name&gt;</c>). Use the versionless form so
    /// that rotating the key needs no configuration change.
    /// </summary>
    public const string KeyIdVariable = "DATA_PROTECTION_KEY_ID";

    public static IDataProtectionBuilder AddApexRacersDataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var keyId = configuration[KeyIdVariable];
        var readsKeyVault = !string.IsNullOrWhiteSpace(configuration["AZURE_KEY_VAULT_URL"]);

        if (string.IsNullOrWhiteSpace(keyId) && readsKeyVault)
            throw new InvalidOperationException(
                $"{KeyIdVariable} is not set. A host that reads Key Vault must wrap the Data Protection "
                + "key ring with a Key Vault key; without one, a read-only copy of the database could "
                + "forge password-reset links.");

        Uri? keyUri = null;
        if (!string.IsNullOrWhiteSpace(keyId)
            && (!Uri.TryCreate(keyId.Trim(), UriKind.Absolute, out keyUri) || keyUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"{KeyIdVariable} must be an absolute https Key Vault key identifier.");

        var builder = services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<AppDbContext>();

        if (keyUri is not null)
            builder.ProtectKeysWithAzureKeyVault(keyUri, new DefaultAzureCredential());

        return builder;
    }
}
