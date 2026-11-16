using ApexRacers.Api.Services;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// Email-confirmation and password-reset tokens are protected by the Data Protection key ring. On a
/// host that scales to zero, the process that issued a link is routinely gone by the time the link
/// is followed. Each provider here stands in for one process lifetime of the API: both go through
/// the API's real registration and share nothing except the database.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class DataProtectionPersistenceTests(PostgreSqlFixture postgres)
{
    private static ServiceProvider BuildApiProcess(DbContextOptions<AppDbContext> dbOptions) =>
        BuildProcess(dbOptions, services => services.AddApexRacersDataProtection(LocalConfiguration));

    // Local Compose and CI: no Key Vault, so the key ring is stored without a key-encryption key.
    private static readonly IConfiguration LocalConfiguration = new ConfigurationBuilder().Build();

    private static IConfiguration Settings(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    // The host as it was before persistence: Data Protection with no key store of its own.
    private static ServiceProvider BuildEphemeralProcess(DbContextOptions<AppDbContext> dbOptions) =>
        BuildProcess(dbOptions, services => services.AddDataProtection()
            .SetApplicationName(DataProtectionRegistration.ApplicationName));

    private static ServiceProvider BuildProcess(
        DbContextOptions<AppDbContext> dbOptions,
        Action<IServiceCollection> addDataProtection)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new AppDbContext(dbOptions));
        addDataProtection(services);
        // A fresh container starts with an empty, private local disk. With no key store configured,
        // Data Protection would fall back to a per-user key directory, which on a test machine every
        // "process" shares, and the restart would go unnoticed. Give each process its own empty one,
        // so only a store that really outlives the process can make a link survive.
        services.PostConfigure<KeyManagementOptions>(o => o.XmlRepository ??= new ProcessLocalKeyRepository());
        services.AddIdentityCore<ApplicationUser>(o => o.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    private static async Task<(Guid Id, T Token)> CreateUserAsync<T>(
        ServiceProvider process,
        Func<UserManager<ApplicationUser>, ApplicationUser, Task<T>> issue)
    {
        using var scope = process.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "racer@example.com", Email = "racer@example.com", DisplayName = "Racer" };
        var created = await users.CreateAsync(user, "Correct-Horse-9");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        return (user.Id, await issue(users, user));
    }

    private static async Task<IdentityResult> InNewScopeAsync(
        ServiceProvider process,
        Guid userId,
        Func<UserManager<ApplicationUser>, ApplicationUser, Task<IdentityResult>> redeem)
    {
        using var scope = process.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        return await redeem(users, user);
    }

    [Fact]
    public async Task Email_confirmation_link_issued_before_a_restart_confirms_after_it()
    {
        var dbOptions = postgres.CreateOptions();

        Guid userId;
        string token;
        using (var before = BuildApiProcess(dbOptions))
            (userId, token) = await CreateUserAsync(before, (u, user) => u.GenerateEmailConfirmationTokenAsync(user));

        using var after = BuildApiProcess(dbOptions);
        var result = await InNewScopeAsync(after, userId, (u, user) => u.ConfirmEmailAsync(user, token));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    // The control: without the persistent store the same two process lifetimes do not share keys, so
    // the link fails. This is what the host did before, and it proves the harness above cannot pass
    // by sharing a key ring some other way (a per-user key directory on the test machine, say).
    [Fact]
    public async Task Without_the_persistent_key_store_a_link_does_not_survive_a_restart()
    {
        var dbOptions = postgres.CreateOptions();

        Guid userId;
        string token;
        using (var before = BuildEphemeralProcess(dbOptions))
            (userId, token) = await CreateUserAsync(before, (u, user) => u.GenerateEmailConfirmationTokenAsync(user));

        using var after = BuildEphemeralProcess(dbOptions);
        var result = await InNewScopeAsync(after, userId, (u, user) => u.ConfirmEmailAsync(user, token));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Password_reset_link_issued_before_a_restart_resets_after_it()
    {
        var dbOptions = postgres.CreateOptions();

        Guid userId;
        string token;
        using (var before = BuildApiProcess(dbOptions))
            (userId, token) = await CreateUserAsync(before, (u, user) => u.GeneratePasswordResetTokenAsync(user));

        using var after = BuildApiProcess(dbOptions);
        var result = await InNewScopeAsync(after, userId, (u, user) => u.ResetPasswordAsync(user, token, "Another-Horse-7"));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    // A deployed host reads Key Vault (AZURE_KEY_VAULT_URL). There a key ring stored in the clear
    // would let a read-only copy of the database forge reset links, so the key-encryption key is
    // required rather than optional, and its absence stops startup instead of degrading silently.
    [Fact]
    public void A_deployed_host_without_a_key_encryption_key_refuses_to_start()
    {
        var deployed = Settings(("AZURE_KEY_VAULT_URL", "https://example-vault.vault.azure.net"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddApexRacersDataProtection(deployed));

        Assert.Contains(DataProtectionRegistration.KeyIdVariable, error.Message);
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("http://example-vault.vault.azure.net/keys/data-protection")]
    public void A_malformed_key_encryption_key_identifier_refuses_to_start(string keyId)
    {
        var configured = Settings((DataProtectionRegistration.KeyIdVariable, keyId));

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddApexRacersDataProtection(configured));

        Assert.Contains(DataProtectionRegistration.KeyIdVariable, error.Message);
    }

    [Fact]
    public void A_key_encryption_key_identifier_is_accepted_without_contacting_key_vault()
    {
        var configured = Settings(
            ("AZURE_KEY_VAULT_URL", "https://example-vault.vault.azure.net"),
            (DataProtectionRegistration.KeyIdVariable, "https://example-vault.vault.azure.net/keys/data-protection"));

        var builder = new ServiceCollection().AddApexRacersDataProtection(configured);

        Assert.NotNull(builder);
    }

    private sealed class ProcessLocalKeyRepository : IXmlRepository
    {
        private readonly List<XElement> _elements = [];

        public IReadOnlyCollection<XElement> GetAllElements() => _elements.ToList();

        public void StoreElement(XElement element, string friendlyName) => _elements.Add(element);
    }
}
