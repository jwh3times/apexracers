using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Api.Services.Email;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public class AuthServiceTests(PostgreSqlFixture postgres)
{
    // ── Shared setup ─────────────────────────────────────────────────────────

    private ServiceProvider BuildProvider(DbContextOptions<AppDbContext>? options = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Password reset tokens are produced by DataProtectorTokenProvider, which needs
        // data-protection services + the default token providers registered.
        services.AddDataProtection();
        var dbOptions = options ?? postgres.CreateOptions();
        services.AddScoped(_ => new AppDbContext(dbOptions));
        services.AddIdentityCore<ApplicationUser>(o =>
        {
            o.Password.RequireDigit          = false;
            o.Password.RequiredLength        = 4;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequireUppercase      = false;
            o.User.RequireUniqueEmail        = true;
            o.Lockout.AllowedForNewUsers      = true;
            o.Lockout.MaxFailedAccessAttempts = 3;
            o.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(15);
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    private static AuthService BuildService(ServiceProvider provider, IEmailSender? emailSender = null)
    {
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var db          = provider.GetRequiredService<AppDbContext>();
        var config      = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_SIGNING_KEY"] = "unit-test-signing-key-minimum-32-bytes-long!",
                ["APP_BASE_URL"]    = "https://test.apexracers.gg"
            })
            .Build();
        // Bound the same way production binds it, so the tests exercise the real defaults rather
        // than a second set invented here.
        var jwt = JwtSettings.FromConfiguration(config);
        var refreshTokens = new RefreshTokenStore(db, TimeProvider.System, NullLogger<RefreshTokenStore>.Instance);
        return new AuthService(
            userManager, config, jwt, refreshTokens, emailSender ?? new FakeEmailSender());
    }

    private static async Task SeedRolesAsync(ServiceProvider provider)
    {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "Standard", "Beta", "Alpha", "Admin" })
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
    }

    /// <summary>
    /// Registers, confirms the address, and signs in — the whole path an account now takes to become
    /// usable. Tests that only need an established user call this instead of reading a token straight
    /// out of <c>RegisterAsync</c>, which no longer returns one.
    /// </summary>
    private static async Task<AuthResultDto> RegisterAndSignInAsync(
        ServiceProvider provider, AuthService svc, string email, string password, CancellationToken ct)
    {
        await svc.RegisterAsync(new RegisterRequest(email, password), ct);
        await ConfirmRegisteredEmailAsync(provider, svc, email, ct);

        var login = await svc.LoginAsync(new LoginRequest(email, password), ct);
        return login.Auth
            ?? throw new InvalidOperationException($"Sign-in failed for {email} after confirmation.");
    }

    /// <summary>
    /// Follows the confirmation link the way a recipient would, by minting the same token the email
    /// carries. Reading it out of the <see cref="FakeEmailSender"/> would tie every caller to the
    /// template's link format for no added coverage.
    /// </summary>
    private static async Task ConfirmRegisteredEmailAsync(
        ServiceProvider provider, AuthService svc, string email, CancellationToken ct)
    {
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"Registration did not create an account for {email}.");
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await svc.ConfirmEmailAsync(user.Id, token, ct);
    }

    // ── RegisterAsync ─────────────────────────────────────────────────────────
    //
    // Registration is enumeration-safe (GHSA-72v6-mw4c-q96r): every reachable outcome has to look
    // the same to the caller. Two things carry that, and both are asserted below — the call returns
    // nothing whether the address was free or taken, and the account it creates cannot sign in until
    // the emailed link is followed, so a follow-up sign-in does not leak the answer either.

    [Fact]
    public async Task RegisterAsync_NewAddress_CreatesUnconfirmedAccountAndEmailsTheConfirmationLink()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);

        await svc.RegisterAsync(new RegisterRequest("jerry@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("jerry@example.com");
        Assert.NotNull(user);
        Assert.Equal("jerry", user.DisplayName);
        Assert.False(user.EmailConfirmed);

        var sent = Assert.Single(emails.Sent);
        Assert.Equal("jerry@example.com", sent.To);
        Assert.Contains("Confirm your ApexRacers email", sent.Subject);
        Assert.Contains("/verify-email?userId=", sent.TextBody);
    }

    [Fact]
    public async Task RegisterAsync_NewAddress_IssuesNoSession()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await svc.RegisterAsync(new RegisterRequest("jerry@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        // The method returns nothing, so the only way a session could still leak out is a refresh
        // token left behind in the store. There must be none: the account is not usable yet.
        var db = provider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.RefreshTokens.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterAsync_TakenAddress_DoesNotThrowAndCreatesNoSecondAccount()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await RegisterAndSignInAsync(provider, svc, "dup@example.com", "Pass1234", TestContext.Current.CancellationToken);

        // The whole point: indistinguishable from the free-address path. This used to throw with
        // Identity's "Email 'dup@example.com' is already taken."
        await svc.RegisterAsync(new RegisterRequest("dup@example.com", "Different9"), TestContext.Current.CancellationToken);

        var db = provider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "dup@example.com", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterAsync_TakenAddress_LeavesTheExistingPasswordIntact()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await RegisterAndSignInAsync(provider, svc, "dup@example.com", "Pass1234", TestContext.Current.CancellationToken);
        await svc.RegisterAsync(new RegisterRequest("dup@example.com", "Attacker9"), TestContext.Current.CancellationToken);

        // Silence must not mean "quietly took the new password" — that would trade a disclosed
        // error for account takeover.
        Assert.NotNull((await svc.LoginAsync(new LoginRequest("dup@example.com", "Pass1234"), TestContext.Current.CancellationToken)).Auth);
        Assert.Null((await svc.LoginAsync(new LoginRequest("dup@example.com", "Attacker9"), TestContext.Current.CancellationToken)).Auth);
    }

    [Fact]
    public async Task RegisterAsync_TakenConfirmedAddress_NotifiesTheOwnerInstead()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);

        await RegisterAndSignInAsync(provider, svc, "owner@example.com", "Pass1234", TestContext.Current.CancellationToken);
        emails.Clear();

        await svc.RegisterAsync(new RegisterRequest("owner@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        var sent = Assert.Single(emails.Sent);
        Assert.Equal("owner@example.com", sent.To);
        Assert.Contains("Someone tried to create an ApexRacers account", sent.Subject);
    }

    [Fact]
    public async Task RegisterAsync_TakenUnconfirmedAddress_ResendsTheConfirmationLink()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);

        await svc.RegisterAsync(new RegisterRequest("retry@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        emails.Clear();

        // Almost always the same person retrying because the first email never arrived. Telling
        // them to reset a password they already know would be useless.
        await svc.RegisterAsync(new RegisterRequest("retry@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        var sent = Assert.Single(emails.Sent);
        Assert.Contains("Confirm your ApexRacers email", sent.Subject);
    }

    [Fact]
    public async Task RegisterAsync_WeakPassword_SurfacesIdentityErrorDescription()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        // "ab" is shorter than the configured RequiredLength of 4.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RegisterAsync(new RegisterRequest("weak@example.com", "ab"), TestContext.Current.CancellationToken));

        // The message must carry Identity's descriptive reason, not a generic fallback.
        Assert.Contains("least", ex.Message);
    }

    [Fact]
    public async Task RegisterAsync_WeakPassword_FailsIdenticallyForATakenAndAFreeAddress()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await RegisterAndSignInAsync(provider, svc, "taken@example.com", "Pass1234", TestContext.Current.CancellationToken);

        // Password errors still surface, so they must not become the oracle the duplicate message
        // used to be: rejecting a weak password has to read the same either way.
        var onTaken = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RegisterAsync(new RegisterRequest("taken@example.com", "ab"), TestContext.Current.CancellationToken));
        var onFree = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RegisterAsync(new RegisterRequest("free@example.com", "ab"), TestContext.Current.CancellationToken));

        Assert.Equal(onFree.Message, onTaken.Message);
        Assert.DoesNotContain("taken@example.com", onTaken.Message);
    }

    [Fact]
    public async Task RegisterAsync_SignedInToken_ContainsEmailNameAndRoleClaims()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var result = await RegisterAndSignInAsync(provider, svc, "driver@example.com", "Pass1234", TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Sub   && Guid.TryParse(c.Value, out _));
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "driver@example.com");
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Name  && c.Value == "driver");
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "Standard");
    }

    // ── ConfirmEmailAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmEmailAsync_ValidToken_EnablesSignIn()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await svc.RegisterAsync(new RegisterRequest("new@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        Assert.Null((await svc.LoginAsync(new LoginRequest("new@example.com", "Pass1234"), TestContext.Current.CancellationToken)).Auth);

        await ConfirmRegisteredEmailAsync(provider, svc, "new@example.com", TestContext.Current.CancellationToken);

        Assert.NotNull((await svc.LoginAsync(new LoginRequest("new@example.com", "Pass1234"), TestContext.Current.CancellationToken)).Auth);
    }

    [Fact]
    public async Task ConfirmEmailAsync_AlreadyConfirmed_SucceedsWithoutThrowing()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await RegisterAndSignInAsync(provider, svc, "twice@example.com", "Pass1234", TestContext.Current.CancellationToken);

        // A second click on the same link — a mail client prefetching it is enough — is not an error.
        await ConfirmRegisteredEmailAsync(provider, svc, "twice@example.com", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConfirmEmailAsync_InvalidToken_ThrowsWithoutConfirming()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await svc.RegisterAsync(new RegisterRequest("bad@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("bad@example.com");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ConfirmEmailAsync(user!.Id, "not-a-real-token", TestContext.Current.CancellationToken));

        Assert.Null((await svc.LoginAsync(new LoginRequest("bad@example.com", "Pass1234"), TestContext.Current.CancellationToken)).Auth);
    }

    [Fact]
    public async Task ConfirmEmailAsync_UnknownUser_Throws()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ConfirmEmailAsync(Guid.NewGuid(), "any-token", TestContext.Current.CancellationToken));
    }

    // ── LoginAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_CorrectCredentials_ReturnsAuthResult()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await RegisterAndSignInAsync(provider, svc, "user@example.com", "Pass1234", TestContext.Current.CancellationToken);

        var result = await svc.LoginAsync(new LoginRequest("user@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        Assert.False(result.LockedOut);
        Assert.NotNull(result.Auth);
        Assert.NotEmpty(result.Auth!.Token);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsInvalidNotLocked()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await RegisterAndSignInAsync(provider, svc, "user@example.com", "Pass1234", TestContext.Current.CancellationToken);

        var result = await svc.LoginAsync(new LoginRequest("user@example.com", "WrongPassword"), TestContext.Current.CancellationToken);

        Assert.Null(result.Auth);
        Assert.False(result.LockedOut);
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_ReturnsInvalid()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        var result = await svc.LoginAsync(new LoginRequest("nobody@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        Assert.Null(result.Auth);
        Assert.False(result.LockedOut);
    }

    [Fact]
    public async Task LoginAsync_ExceedsMaxFailedAttempts_LocksOutEvenWithCorrectPassword()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await RegisterAndSignInAsync(provider, svc, "locked@example.com", "Pass1234", TestContext.Current.CancellationToken);

        // MaxFailedAccessAttempts = 3 in the test provider.
        for (var i = 0; i < 3; i++)
            await svc.LoginAsync(new LoginRequest("locked@example.com", "WrongPassword"), TestContext.Current.CancellationToken);

        // Even the correct password is rejected while the account is locked.
        var result = await svc.LoginAsync(new LoginRequest("locked@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        Assert.True(result.LockedOut);
        Assert.Null(result.Auth);
    }

    [Fact]
    public async Task LoginAsync_SuccessAfterFailures_ResetsFailedCount()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await RegisterAndSignInAsync(provider, svc, "reset@example.com", "Pass1234", TestContext.Current.CancellationToken);

        // Two failures (below the threshold of 3), then a success that resets the counter.
        await svc.LoginAsync(new LoginRequest("reset@example.com", "nope"), TestContext.Current.CancellationToken);
        await svc.LoginAsync(new LoginRequest("reset@example.com", "nope"), TestContext.Current.CancellationToken);
        var ok = await svc.LoginAsync(new LoginRequest("reset@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        Assert.NotNull(ok.Auth);

        // Two more failures would have locked the account if the counter had not reset.
        await svc.LoginAsync(new LoginRequest("reset@example.com", "nope"), TestContext.Current.CancellationToken);
        var second = await svc.LoginAsync(new LoginRequest("reset@example.com", "nope"), TestContext.Current.CancellationToken);

        Assert.False(second.LockedOut);
    }

    [Fact]
    public async Task LoginAsync_UnconfirmedAccount_IsIndistinguishableFromAnUnknownEmail()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await svc.RegisterAsync(new RegisterRequest("pending@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        // This is what actually closes the registration oracle. If the credentials an attacker just
        // submitted worked here, success would tell them the address had been free and failure would
        // tell them it was taken — exactly the answer registration withholds.
        var unconfirmed = await svc.LoginAsync(new LoginRequest("pending@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var unknown = await svc.LoginAsync(new LoginRequest("nobody@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        Assert.Equal(unknown, unconfirmed);
        Assert.Null(unconfirmed.Auth);
        Assert.False(unconfirmed.LockedOut);
    }

    [Fact]
    public async Task LoginAsync_UnconfirmedAccount_CannotBeLockedOutByAStranger()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await svc.RegisterAsync(new RegisterRequest("pending@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        // The confirmation check runs ahead of the failure counter, so hammering an address that has
        // not been confirmed cannot burn its lockout budget — otherwise a stranger could keep the
        // real owner locked out from the moment they signed up.
        for (var i = 0; i < 5; i++)
            await svc.LoginAsync(new LoginRequest("pending@example.com", "WrongPassword"), TestContext.Current.CancellationToken);

        await ConfirmRegisteredEmailAsync(provider, svc, "pending@example.com", TestContext.Current.CancellationToken);

        var result = await svc.LoginAsync(new LoginRequest("pending@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        Assert.NotNull(result.Auth);
        Assert.False(result.LockedOut);
    }

    // ── UpdateProfileAsync ────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData(null, "wrong")]
    [InlineData(100L, null)]
    [InlineData(100L, "")]
    [InlineData(100L, "wrong")]
    public async Task UpdateProfileAsync_ChangedClaimWithoutCorrectPassword_RejectsBeforeAnyProfileMutation(
        long? existingCustomerId, string? currentPassword)
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var ct = TestContext.Current.CancellationToken;
        var reg = await RegisterAndSignInAsync(provider, svc, "profile@example.com", "Pass1234", ct);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(reg.UserId.ToString()))!;
        user.IRacingCustomerId = existingCustomerId;
        Assert.True((await userManager.UpdateAsync(user)).Succeeded);
        var originalName = user.DisplayName;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateProfileAsync(
            reg.UserId, new UpdateProfileRequest("Changed", 200L, "dark", currentPassword), ct));

        Assert.Equal("Current password is incorrect.", ex.Message);
        Assert.Equal(originalName, user.DisplayName);
        Assert.Equal(existingCustomerId, user.IRacingCustomerId);
        Assert.Equal("auto", user.ThemePreference);
        var stored = await provider.GetRequiredService<AppDbContext>().Users.AsNoTracking()
            .SingleAsync(u => u.Id == reg.UserId, ct);
        Assert.Equal(originalName, stored.DisplayName);
        Assert.Equal(existingCustomerId, stored.IRacingCustomerId);
        Assert.Equal("auto", stored.ThemePreference);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(100L)]
    public async Task UpdateProfileAsync_ChangedClaimWithCorrectPassword_UpdatesProfile(long? existingCustomerId)
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var ct = TestContext.Current.CancellationToken;
        var reg = await RegisterAndSignInAsync(provider, svc, "profile@example.com", "Pass1234", ct);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(reg.UserId.ToString()))!;
        user.IRacingCustomerId = existingCustomerId;
        Assert.True((await userManager.UpdateAsync(user)).Succeeded);

        await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Changed", 200L, "dark", "Pass1234"), ct);

        var stored = await provider.GetRequiredService<AppDbContext>().Users.AsNoTracking()
            .SingleAsync(u => u.Id == reg.UserId, ct);
        Assert.Equal("Changed", stored.DisplayName);
        Assert.Equal(200L, stored.IRacingCustomerId);
        Assert.Equal("dark", stored.ThemePreference);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(100L)]
    public async Task UpdateProfileAsync_UnchangedClaim_UpdatesNameAndThemeWithoutPassword(long? requestedCustomerId)
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var ct = TestContext.Current.CancellationToken;
        var reg = await RegisterAndSignInAsync(provider, svc, "profile@example.com", "Pass1234", ct);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(reg.UserId.ToString()))!;
        user.IRacingCustomerId = 100L;
        Assert.True((await userManager.UpdateAsync(user)).Succeeded);

        await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Changed", requestedCustomerId, "dark"), ct);

        var stored = await provider.GetRequiredService<AppDbContext>().Users.AsNoTracking()
            .SingleAsync(u => u.Id == reg.UserId, ct);
        Assert.Equal("Changed", stored.DisplayName);
        Assert.Equal(100L, stored.IRacingCustomerId);
        Assert.Equal("dark", stored.ThemePreference);
    }

    [Fact]
    public async Task UpdateProfileAsync_UpdatesDisplayName()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("New Name"), TestContext.Current.CancellationToken);

        Assert.Equal("New Name", result.DisplayName);
    }

    [Fact]
    public async Task UpdateProfileAsync_SavesIRacingCustomerId_WhenProvided()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", 123456789L, CurrentPassword: "Pass1234"), TestContext.Current.CancellationToken);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "iracing_id" && c.Value == "123456789");
    }

    [Fact]
    public async Task UpdateProfileAsync_CustomerIdClaimedByAnotherUser_ThrowsClaimedIdentityConflict()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var first = await RegisterAndSignInAsync(provider, svc, "first@example.com", "Pass1234", TestContext.Current.CancellationToken);
        var second = await RegisterAndSignInAsync(provider, svc, "second@example.com", "Pass1234", TestContext.Current.CancellationToken);
        await svc.UpdateProfileAsync(
            first.UserId,
            new UpdateProfileRequest("First Driver", 123456L, CurrentPassword: "Pass1234"),
            TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<ClaimedIdentityConflictException>(() =>
            svc.UpdateProfileAsync(
                second.UserId,
                new UpdateProfileRequest("Second Driver", 123456L, CurrentPassword: "Pass1234"),
                TestContext.Current.CancellationToken));

        Assert.Equal(ClaimedIdentityConflictException.DefaultMessage, ex.Message);
    }

    [Fact]
    public async Task UpdateProfileAsync_ConcurrentClaims_OneUserWinsAndOneReceivesClaimedIdentityConflict()
    {
        const long customerId = 123456L;
        var barrier = new ConcurrentClaimSaveBarrier(customerId);
        var options = postgres.CreateOptions(barrier);

        Guid firstUserId;
        Guid secondUserId;
        await using (var setupProvider = BuildProvider(options))
        {
            await SeedRolesAsync(setupProvider);
            var setupService = BuildService(setupProvider);
            firstUserId = (await RegisterAndSignInAsync(setupProvider, setupService, "first@example.com", "Pass1234", TestContext.Current.CancellationToken)).UserId;
            secondUserId = (await RegisterAndSignInAsync(setupProvider, setupService, "second@example.com", "Pass1234", TestContext.Current.CancellationToken)).UserId;
        }

        await using var firstProvider = BuildProvider(options);
        await using var secondProvider = BuildProvider(options);
        var firstService = BuildService(firstProvider);
        var secondService = BuildService(secondProvider);

        var outcomes = await Task.WhenAll(
            CaptureAsync(() => firstService.UpdateProfileAsync(
                firstUserId,
                new UpdateProfileRequest("First Driver", customerId, CurrentPassword: "Pass1234"),
                TestContext.Current.CancellationToken)),
            CaptureAsync(() => secondService.UpdateProfileAsync(
                secondUserId,
                new UpdateProfileRequest("Second Driver", customerId, CurrentPassword: "Pass1234"),
                TestContext.Current.CancellationToken)));

        Assert.Single(outcomes, outcome => outcome is null);
        var conflict = Assert.Single(outcomes.OfType<ClaimedIdentityConflictException>());
        Assert.Equal(ClaimedIdentityConflictException.DefaultMessage, conflict.Message);

        await using var verificationDb = new AppDbContext(options);
        Assert.Single(await verificationDb.Users
            .Where(user => user.IRacingCustomerId == customerId)
            .Select(user => user.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateProfileAsync_EmptyDisplayName_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("   "), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateProfileAsync_UnknownUser_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateProfileAsync(Guid.NewGuid(), new UpdateProfileRequest("Name"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateProfileAsync_ValidThemePreference_SetsThemeClaim()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", ThemePreference: "dark"), TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "theme_preference" && c.Value == "dark");
    }

    [Fact]
    public async Task UpdateProfileAsync_InvalidThemePreference_LeavesDefaultTheme()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);
        // "neon" is not a valid theme — the second condition is false, so the default "auto" is preserved.
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", ThemePreference: "neon"), TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "theme_preference" && c.Value == "auto");
    }

    [Fact]
    public async Task UpdateProfileAsync_DoesNotClearIRacingCustomerId_WhenNotProvided()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);
        await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name", 100042L, CurrentPassword: "Pass1234"), TestContext.Current.CancellationToken);

        // Second update without IRacingCustomerId — should not clear the first one
        var result = await svc.UpdateProfileAsync(reg.UserId, new UpdateProfileRequest("Name Two"), TestContext.Current.CancellationToken);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "iracing_id" && c.Value == "100042");
    }

    [Fact]
    public async Task RegisterAsync_Token_DoesNotContainIRacingIdClaim_ForNewUser()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var result = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "iracing_id");
    }

    // ── UpdateRoleAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRoleAsync_ToBeta_ReturnsTokenWithBetaRole()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);
        var result = await svc.UpdateRoleAsync(reg.UserId, "Beta", TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "Beta");
    }

    [Fact]
    public async Task UpdateRoleAsync_BackToStandard_ReturnsTokenWithStandardRole()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);
        await svc.UpdateRoleAsync(reg.UserId, "Alpha", TestContext.Current.CancellationToken);
        var result = await svc.UpdateRoleAsync(reg.UserId, "Standard", TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "Standard");
    }

    [Fact]
    public async Task UpdateRoleAsync_ToAdmin_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateRoleAsync(reg.UserId, "Admin", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateRoleAsync_AdminCannotSelfDemote()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var reg = await RegisterAndSignInAsync(provider, svc, "admin@example.com", "Pass1234", TestContext.Current.CancellationToken);

        // Promote to Admin via UserManager directly (as the startup seed would)
        var user = await userManager.FindByIdAsync(reg.UserId.ToString());
        var roles = await userManager.GetRolesAsync(user!);
        await userManager.RemoveFromRolesAsync(user!, roles);
        await userManager.AddToRoleAsync(user!, "Admin");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateRoleAsync(reg.UserId, "Standard", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateRoleAsync_UnknownUser_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        // "Beta" is a valid role, so the role-validation check passes and the
        // failure must come from the missing user lookup.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateRoleAsync(Guid.NewGuid(), "Beta", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateRoleAsync_UnsupportedRole_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateRoleAsync(reg.UserId, "Superuser", TestContext.Current.CancellationToken));
    }

    // ── UpdateThemeAsync ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("auto")]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task UpdateThemeAsync_ValidTheme_SetsThemePreferenceClaim(string theme)
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);
        var result = await svc.UpdateThemeAsync(reg.UserId, theme, TestContext.Current.CancellationToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "theme_preference" && c.Value == theme);
    }

    [Fact]
    public async Task UpdateThemeAsync_InvalidTheme_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "u@example.com", "Pass1234", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateThemeAsync(reg.UserId, "neon", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateThemeAsync_UnknownUser_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        // "dark" is valid, so the failure must come from the missing user lookup.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateThemeAsync(Guid.NewGuid(), "dark", TestContext.Current.CancellationToken));
    }

    // ── HandleCallbackAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task HandleCallbackAsync_ThrowsNotImplementedException()
    {
        await using var provider = BuildProvider();
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            svc.HandleCallbackAsync("code", "state", TestContext.Current.CancellationToken));
    }

    // ── RefreshAsync / RevokeAsync ────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_ReturnsRefreshToken()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var result = await RegisterAndSignInAsync(provider, svc, "driver@example.com", "Pass1234", TestContext.Current.CancellationToken);

        Assert.NotNull(result.RefreshToken);
        Assert.NotEmpty(result.RefreshToken!);
    }

    [Fact]
    public async Task LoginAsync_CorrectCredentials_ReturnsRefreshToken()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await RegisterAndSignInAsync(provider, svc, "driver@example.com", "Pass1234", TestContext.Current.CancellationToken);
        var result = await svc.LoginAsync(new LoginRequest("driver@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        Assert.NotNull(result.Auth);
        Assert.NotNull(result.Auth!.RefreshToken);
        Assert.NotEmpty(result.Auth.RefreshToken!);
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_ReturnsNewAccessAndRefreshTokens()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg     = await RegisterAndSignInAsync(provider, svc, "driver@example.com", "Pass1234", TestContext.Current.CancellationToken);
        var originalRefresh = reg.RefreshToken!;

        var refreshed = await svc.RefreshAsync(originalRefresh, TestContext.Current.CancellationToken);

        // Access token must be non-empty
        Assert.NotEmpty(refreshed.Token);
        // New refresh token must be non-empty and different from the one that was rotated out
        Assert.NotNull(refreshed.RefreshToken);
        Assert.NotEmpty(refreshed.RefreshToken!);
        Assert.NotEqual(originalRefresh, refreshed.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_RotatesToken_OldTokenIsRevoked()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg     = await RegisterAndSignInAsync(provider, svc, "driver@example.com", "Pass1234", TestContext.Current.CancellationToken);
        var oldRefresh = reg.RefreshToken!;

        // Rotate once — this revokes oldRefresh
        await svc.RefreshAsync(oldRefresh, TestContext.Current.CancellationToken);

        // Re-using the old token must throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshAsync(oldRefresh, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefreshAsync_InvalidToken_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshAsync("this-token-does-not-exist", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RevokeAsync_ValidToken_SubsequentRefreshThrows()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var login = await RegisterAndSignInAsync(
            provider, svc, "driver@example.com", "Pass1234", TestContext.Current.CancellationToken);

        var refreshToken = login.RefreshToken!;

        await svc.RevokeAsync(refreshToken, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshAsync(refreshToken, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RevokeAsync_UnknownToken_IsIgnoredAndLeavesValidTokensUsable()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await RegisterAndSignInAsync(provider, svc, "driver@example.com", "Pass1234", TestContext.Current.CancellationToken);
        var login = await svc.LoginAsync(
            new LoginRequest("driver@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var refreshToken = login.Auth!.RefreshToken!;

        // Unknown tokens are silently ignored. Holding a live session across the call is
        // what gives that meaning: without it the test asserted only "did not throw", and
        // would have passed against a RevokeAsync that revoked every token it could find.
        await svc.RevokeAsync("garbage", TestContext.Current.CancellationToken);

        Assert.NotNull(await svc.RefreshAsync(refreshToken, TestContext.Current.CancellationToken));
    }

    // ── PurgeExpiredRefreshTokensAsync ────────────────────────────────────────

    [Fact]
    public async Task PurgeExpiredRefreshTokensAsync_RemovesTokensExpiredBeyondRetention()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var db  = provider.GetRequiredService<AppDbContext>();

        await RegisterAndSignInAsync(provider, svc, "driver@example.com", "Pass1234", TestContext.Current.CancellationToken);

        // Age the issued token so it expired 40 days ago (> 30-day retention).
        var token = db.RefreshTokens.Single();
        token.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-40);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var removed = await svc.PurgeExpiredRefreshTokensAsync(TimeSpan.FromDays(30), TestContext.Current.CancellationToken);

        Assert.Equal(1, removed);
        Assert.Empty(db.RefreshTokens);
    }

    [Fact]
    public async Task PurgeExpiredRefreshTokensAsync_KeepsTokensExpiredWithinRetention()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var db  = provider.GetRequiredService<AppDbContext>();

        await RegisterAndSignInAsync(provider, svc, "driver@example.com", "Pass1234", TestContext.Current.CancellationToken);

        // Expired 10 days ago — within the 30-day retention window, so it stays.
        var token = db.RefreshTokens.Single();
        token.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-10);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var removed = await svc.PurgeExpiredRefreshTokensAsync(TimeSpan.FromDays(30), TestContext.Current.CancellationToken);

        Assert.Equal(0, removed);
        Assert.Single(db.RefreshTokens);
    }

    [Fact]
    public async Task PurgeExpiredRefreshTokensAsync_NoTokens_ReturnsZero()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var removed = await svc.PurgeExpiredRefreshTokensAsync(TimeSpan.FromDays(30), TestContext.Current.CancellationToken);

        Assert.Equal(0, removed);
    }

    // ── ChangePasswordAsync (T4) ──────────────────────────────────────────────

    [Fact]
    public async Task ChangePasswordAsync_Success_RevokesAllPriorRefreshTokensOnlyForTheUser()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var ct = TestContext.Current.CancellationToken;
        var reg = await RegisterAndSignInAsync(provider, svc, "change@example.com", "OldPass1", ct);
        var firstLogin = await svc.LoginAsync(new LoginRequest("change@example.com", "OldPass1"), ct);
        var secondLogin = await svc.LoginAsync(new LoginRequest("change@example.com", "OldPass1"), ct);
        var otherUser = await RegisterAndSignInAsync(provider, svc, "other@example.com", "OtherPass1", ct);
        var priorTokens = new[] { reg.RefreshToken!, firstLogin.Auth!.RefreshToken!, secondLogin.Auth!.RefreshToken! };

        await svc.ChangePasswordAsync(reg.UserId, new ChangePasswordRequest("OldPass1", "NewPass2"), ct);

        var db = provider.GetRequiredService<AppDbContext>();
        var rows = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == reg.UserId).ToListAsync(ct);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, token => Assert.NotNull(token.RevokedAt));
        foreach (var token in priorTokens)
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RefreshAsync(token, ct));

        var otherRow = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.UserId == otherUser.UserId, ct);
        Assert.Null(otherRow.RevokedAt);
        Assert.Equal(otherUser.UserId, (await svc.RefreshAsync(otherUser.RefreshToken!, ct)).UserId);
    }

    [Theory]
    [InlineData("WrongOld", "NewPass2")]
    [InlineData("OldPass1", "ab")]
    public async Task ChangePasswordAsync_Failure_PreservesPriorRefreshTokens(string currentPassword, string newPassword)
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var ct = TestContext.Current.CancellationToken;
        var reg = await RegisterAndSignInAsync(provider, svc, "unchanged@example.com", "OldPass1", ct);
        var login = await svc.LoginAsync(new LoginRequest("unchanged@example.com", "OldPass1"), ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ChangePasswordAsync(reg.UserId, new ChangePasswordRequest(currentPassword, newPassword), ct));

        var rows = await provider.GetRequiredService<AppDbContext>().RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == reg.UserId).ToListAsync(ct);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, token => Assert.Null(token.RevokedAt));
        Assert.Equal(reg.UserId, (await svc.RefreshAsync(reg.RefreshToken!, ct)).UserId);
        Assert.Equal(reg.UserId, (await svc.RefreshAsync(login.Auth!.RefreshToken!, ct)).UserId);
    }

    [Fact]
    public async Task ChangePasswordAsync_CorrectCurrentPassword_SwapsTheLoginPassword()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "chg@example.com", "OldPass1", TestContext.Current.CancellationToken);
        await svc.ChangePasswordAsync(reg.UserId, new ChangePasswordRequest("OldPass1", "NewPass2"), TestContext.Current.CancellationToken);

        Assert.NotNull((await svc.LoginAsync(new LoginRequest("chg@example.com", "NewPass2"), TestContext.Current.CancellationToken)).Auth);
        Assert.Null((await svc.LoginAsync(new LoginRequest("chg@example.com", "OldPass1"), TestContext.Current.CancellationToken)).Auth);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "chg2@example.com", "OldPass1", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ChangePasswordAsync(reg.UserId, new ChangePasswordRequest("WrongOld", "NewPass2"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChangePasswordAsync_WeakNewPassword_SurfacesIdentityErrorDescription()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        var reg = await RegisterAndSignInAsync(provider, svc, "chg3@example.com", "OldPass1", TestContext.Current.CancellationToken);

        // "ab" is shorter than the configured RequiredLength of 4.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ChangePasswordAsync(reg.UserId, new ChangePasswordRequest("OldPass1", "ab"), TestContext.Current.CancellationToken));

        Assert.Contains("least", ex.Message);
    }

    [Fact]
    public async Task ChangePasswordAsync_UnknownUser_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ChangePasswordAsync(Guid.NewGuid(), new ChangePasswordRequest("OldPass1", "NewPass2"), TestContext.Current.CancellationToken));
    }

    // ── Password reset (T4) ───────────────────────────────────────────────────

    [Fact]
    public async Task RequestPasswordResetAsync_ExistingUser_EmailsAUsableToken()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);

        await RegisterAndSignInAsync(provider, svc, "forgot@example.com", "Pass1234", TestContext.Current.CancellationToken);
        await svc.RequestPasswordResetAsync("forgot@example.com", TestContext.Current.CancellationToken);

        // The emailed link is the only place the token appears — the method returns nothing.
        Assert.False(string.IsNullOrEmpty(EmailLinks.TokenFrom(emails.Last!)));
    }

    [Fact]
    public async Task RequestPasswordResetAsync_KnownUser_SendsEmailWithTokenLink()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);
        await RegisterAndSignInAsync(provider, svc, "reset@example.com", "Pass1234", TestContext.Current.CancellationToken);

        await svc.RequestPasswordResetAsync("reset@example.com", TestContext.Current.CancellationToken);

        Assert.NotNull(emails.Last);
        Assert.Equal("reset@example.com", emails.Last!.To);
        Assert.Contains("https://test.apexracers.gg/reset-password", emails.Last.HtmlBody);
        Assert.Contains(Uri.EscapeDataString(EmailLinks.TokenFrom(emails.Last)), emails.Last.HtmlBody);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_UnknownUser_SendsNothing()
    {
        await using var provider = BuildProvider();
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);

        await svc.RequestPasswordResetAsync("nobody@example.com", TestContext.Current.CancellationToken);

        Assert.Empty(emails.Sent);
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidToken_SwapsTheLoginPassword()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);

        await RegisterAndSignInAsync(provider, svc, "reset@example.com", "OldPass1", TestContext.Current.CancellationToken);
        await svc.RequestPasswordResetAsync("reset@example.com", TestContext.Current.CancellationToken);
        var token = EmailLinks.TokenFrom(emails.Last!);

        await svc.ResetPasswordAsync(new ResetPasswordRequest("reset@example.com", token, "NewPass99"), TestContext.Current.CancellationToken);

        Assert.NotNull((await svc.LoginAsync(new LoginRequest("reset@example.com", "NewPass99"), TestContext.Current.CancellationToken)).Auth);
        Assert.Null((await svc.LoginAsync(new LoginRequest("reset@example.com", "OldPass1"), TestContext.Current.CancellationToken)).Auth);
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidToken_ConfirmsAnUnconfirmedAddress()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);

        // Registered but never confirmed — the confirmation email went astray.
        await svc.RegisterAsync(new RegisterRequest("lost@example.com", "OldPass1"), TestContext.Current.CancellationToken);
        await svc.RequestPasswordResetAsync("lost@example.com", TestContext.Current.CancellationToken);
        var token = EmailLinks.TokenFrom(emails.Last!);

        await svc.ResetPasswordAsync(new ResetPasswordRequest("lost@example.com", token, "NewPass99"), TestContext.Current.CancellationToken);

        // Following the reset link proves control of the mailbox just as the confirmation link does,
        // so it has to leave the account usable. Without this, a reset would succeed and sign-in
        // would still be refused, with no self-service way out.
        Assert.NotNull((await svc.LoginAsync(new LoginRequest("lost@example.com", "NewPass99"), TestContext.Current.CancellationToken)).Auth);
    }

    [Fact]
    public async Task ResetPasswordAsync_InvalidToken_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await RegisterAndSignInAsync(provider, svc, "badtoken@example.com", "Pass1234", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ResetPasswordAsync(new ResetPasswordRequest("badtoken@example.com", "not-a-real-token", "NewPass99"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResetPasswordAsync_UnknownEmail_ThrowsInvalidOperationException()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ResetPasswordAsync(new ResetPasswordRequest("ghost@example.com", "tok", "NewPass99"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidToken_RevokesActiveRefreshTokens()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);

        await RegisterAndSignInAsync(provider, svc, "revoke@example.com", "OldPass1", TestContext.Current.CancellationToken);
        var login = await svc.LoginAsync(new LoginRequest("revoke@example.com", "OldPass1"), TestContext.Current.CancellationToken);
        var refreshToken = login.Auth!.RefreshToken!;

        await svc.RequestPasswordResetAsync("revoke@example.com", TestContext.Current.CancellationToken);
        var token = EmailLinks.TokenFrom(emails.Last!);
        await svc.ResetPasswordAsync(new ResetPasswordRequest("revoke@example.com", token, "NewPass99"), TestContext.Current.CancellationToken);

        // Every refresh token issued before the reset is now revoked, so it can't be exchanged.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshAsync(refreshToken, TestContext.Current.CancellationToken));
    }

    // ── RequestEmailChangeAsync (C1) ─────────────────────────────────────────

    [Theory]
    [InlineData(null, "new@example.com")]
    [InlineData("", "new@example.com")]
    [InlineData("wrong", "new@example.com")]
    [InlineData(null, "taken@example.com")]
    [InlineData("", "taken@example.com")]
    [InlineData("wrong", "taken@example.com")]
    public async Task RequestEmailChangeAsync_WithoutCorrectPassword_RejectsAndSendsNothing(
        string? currentPassword, string newEmail)
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);
        var ct = TestContext.Current.CancellationToken;
        var reg = await RegisterAndSignInAsync(provider, svc, "old@example.com", "Pass1234", ct);
        await RegisterAndSignInAsync(provider, svc, "taken@example.com", "Pass1234", ct);
        // Registration emails a confirmation link, so drop the setup traffic before asserting
        // on what this call sends.
        emails.Clear();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RequestEmailChangeAsync(reg.UserId, newEmail, currentPassword, ct));

        Assert.Equal("Current password is incorrect.", ex.Message);
        Assert.Empty(emails.Sent);
        var stored = await provider.GetRequiredService<AppDbContext>().Users.AsNoTracking()
            .SingleAsync(u => u.Id == reg.UserId, ct);
        Assert.Equal("old@example.com", stored.Email);
        Assert.Equal("old@example.com", stored.UserName);
    }

    [Fact]
    public async Task RequestEmailChangeAsync_NewAddress_SendsVerificationToNewEmail()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);
        var reg = await RegisterAndSignInAsync(provider, svc, "old@example.com", "Pass1234", TestContext.Current.CancellationToken);

        await svc.RequestEmailChangeAsync(reg.UserId, "new@example.com", "Pass1234", TestContext.Current.CancellationToken);

        var verification = Assert.Single(emails.Sent, e => e.To == "new@example.com");
        Assert.Contains("https://test.apexracers.gg/verify-email", verification.HtmlBody);
        Assert.Contains(reg.UserId.ToString(), verification.HtmlBody);

        var link = new Uri(Assert.Single(verification.TextBody.Split('\n'), line => line.StartsWith("https://")));
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(link.Query);
        await svc.ConfirmEmailChangeAsync(reg.UserId, "new@example.com", query["token"].ToString(), TestContext.Current.CancellationToken);

        var stored = await provider.GetRequiredService<AppDbContext>().Users.AsNoTracking()
            .SingleAsync(u => u.Id == reg.UserId, TestContext.Current.CancellationToken);
        Assert.Equal("new@example.com", stored.Email);
        Assert.Equal("new@example.com", stored.UserName);
    }

    [Fact]
    public async Task RequestEmailChangeAsync_NewAddress_AlsoNotifiesOldAddress()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);
        var reg = await RegisterAndSignInAsync(provider, svc, "old@example.com", "Pass1234", TestContext.Current.CancellationToken);
        // Registration emails a confirmation link, so drop the setup traffic before asserting
        // on what this call sends.
        emails.Clear();

        await svc.RequestEmailChangeAsync(reg.UserId, "new@example.com", "Pass1234", TestContext.Current.CancellationToken);

        var notice = Assert.Single(emails.Sent, e => e.To == "old@example.com");
        Assert.Contains("new@example.com", notice.HtmlBody);
        Assert.Contains("https://test.apexracers.gg/forgot-password", notice.HtmlBody);
    }

    [Fact]
    public async Task RequestEmailChangeAsync_AddressUsedByAnother_SendsNothing()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);
        await RegisterAndSignInAsync(provider, svc, "taken@example.com", "Pass1234", TestContext.Current.CancellationToken);
        var reg = await RegisterAndSignInAsync(provider, svc, "me@example.com", "Pass1234", TestContext.Current.CancellationToken);
        // Registration emails a confirmation link, so drop the setup traffic before asserting
        // on what this call sends.
        emails.Clear();

        await svc.RequestEmailChangeAsync(reg.UserId, "taken@example.com", "Pass1234", TestContext.Current.CancellationToken);

        Assert.Empty(emails.Sent);
    }

    // ── ConfirmEmailChangeAsync (C2) ─────────────────────────────────────────

    [Fact]
    public async Task ConfirmEmailChangeAsync_ValidToken_ChangesEmailAndUsername()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);
        var reg = await RegisterAndSignInAsync(provider, svc, "old@example.com", "Pass1234", TestContext.Current.CancellationToken);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(reg.UserId.ToString());
        var token = await userManager.GenerateChangeEmailTokenAsync(user!, "new@example.com");

        await svc.ConfirmEmailChangeAsync(reg.UserId, "new@example.com", token, TestContext.Current.CancellationToken);

        var updated = await userManager.FindByIdAsync(reg.UserId.ToString());
        Assert.Equal("new@example.com", updated!.Email);
        Assert.Equal("new@example.com", updated.UserName);
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_BadToken_Throws()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var reg = await RegisterAndSignInAsync(provider, svc, "old@example.com", "Pass1234", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ConfirmEmailChangeAsync(reg.UserId, "new@example.com", "not-a-real-token", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_ValidToken_RevokesActiveRefreshTokens()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var db  = provider.GetRequiredService<AppDbContext>();

        await RegisterAndSignInAsync(provider, svc, "revoke-email@example.com", "OldPass1", TestContext.Current.CancellationToken);
        var login = await svc.LoginAsync(new LoginRequest("revoke-email@example.com", "OldPass1"), TestContext.Current.CancellationToken);
        var refreshToken = login.Auth!.RefreshToken!;

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("revoke-email@example.com");
        var token = await userManager.GenerateChangeEmailTokenAsync(user!, "revoke-email-new@example.com");
        await svc.ConfirmEmailChangeAsync(user!.Id, "revoke-email-new@example.com", token, TestContext.Current.CancellationToken);

        // Every refresh token issued before the email change is now revoked, so it can't be exchanged.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefreshAsync(refreshToken, TestContext.Current.CancellationToken));
    }

    // ── Active refresh-token cap (T5) ─────────────────────────────────────────

    private static int CountActiveTokens(AppDbContext db) =>
        db.RefreshTokens.Count(t => t.RevokedAt == null && t.ExpiresAt > DateTimeOffset.UtcNow);

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class ConcurrentClaimSaveBarrier(long customerId) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<ApplicationUser>()
                    .Any(IsCompetingClaim) is not true)
                return result;

            if (Interlocked.Increment(ref arrivals) == 2)
                release.TrySetResult();

            await release.Task.WaitAsync(cancellationToken);
            return result;
        }

        private bool IsCompetingClaim(EntityEntry<ApplicationUser> entry) =>
            entry.State == EntityState.Modified &&
            entry.Property(user => user.IRacingCustomerId).CurrentValue == customerId;
    }

    [Fact]
    public async Task IssuingRefreshToken_BeyondCap_KeepsActiveCountAtTheCap()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var db  = provider.GetRequiredService<AppDbContext>();

        // Register issues one token; six more logins is seven issuances total,
        // two past the cap of five.
        await RegisterAndSignInAsync(provider, svc, "capped@example.com", "Pass1234", TestContext.Current.CancellationToken);
        for (var i = 0; i < 6; i++)
            await svc.LoginAsync(new LoginRequest("capped@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        // Active count is clamped to the cap; the surplus rows are revoked, not deleted.
        Assert.Equal(5, CountActiveTokens(db));
        Assert.Equal(7, db.RefreshTokens.Count());
    }

    [Fact]
    public async Task IssuingRefreshToken_UnderCap_RevokesNothing()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var db  = provider.GetRequiredService<AppDbContext>();

        // One register + three logins = four active tokens, one under the cap.
        await RegisterAndSignInAsync(provider, svc, "under@example.com", "Pass1234", TestContext.Current.CancellationToken);
        for (var i = 0; i < 3; i++)
            await svc.LoginAsync(new LoginRequest("under@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        Assert.Equal(4, CountActiveTokens(db));
        Assert.All(db.RefreshTokens, t => Assert.Null(t.RevokedAt));
    }

    [Fact]
    public async Task IssuingRefreshToken_BeyondCap_RevokesTheOldestActiveToken()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var db  = provider.GetRequiredService<AppDbContext>();

        // Get to exactly the cap (five active tokens).
        await RegisterAndSignInAsync(provider, svc, "oldest@example.com", "Pass1234", TestContext.Current.CancellationToken);
        for (var i = 0; i < 4; i++)
            await svc.LoginAsync(new LoginRequest("oldest@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        // Give the five rows strictly increasing creation times so "oldest" is unambiguous.
        var rows = db.RefreshTokens.OrderBy(t => t.CreatedAt).ToList();
        Assert.Equal(5, rows.Count);
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        for (var i = 0; i < rows.Count; i++)
            rows[i].CreatedAt = baseTime.AddSeconds(i);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var oldestId = rows[0].Id;

        // The sixth issuance trips the cap and must revoke the oldest active token.
        await svc.LoginAsync(new LoginRequest("oldest@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        var oldest = db.RefreshTokens.Single(t => t.Id == oldestId);
        Assert.NotNull(oldest.RevokedAt);
        Assert.Equal(5, CountActiveTokens(db));
    }
}
