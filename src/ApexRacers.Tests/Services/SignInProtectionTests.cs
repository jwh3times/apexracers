using System.Data.Common;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Api.Services.Email;
using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public class SignInProtectionTests(PostgreSqlFixture postgres)
{
    private ServiceProvider BuildProvider(
        DbContextOptions<AppDbContext>? options = null,
        IPasswordHasher<ApplicationUser>? passwordHasher = null)
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
            o.Password.RequireDigit = false;
            o.Password.RequiredLength = 4;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequireUppercase = false;
            o.User.RequireUniqueEmail = true;
            // Identity's account-wide lockout stays off here exactly as it is in Program.cs.
            // Throttling is SignInThrottleStore's job now, and it counts per source address.
            o.Lockout.AllowedForNewUsers = false;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();
        // Registered after Identity so it replaces the default, letting a test observe how much
        // password work a request actually does.
        if (passwordHasher is not null)
            services.AddSingleton(passwordHasher);
        return services.BuildServiceProvider();
    }

    private static readonly SignInThrottleOptions TestThrottleOptions =
        SignInThrottle.Defaults with { PerAddressMaxFailures = 3 };
    private const string Guesser = "203.0.113.7";
    private const string Owner = "198.51.100.20";
    private static string FreshAddress(int i) => "10.0.0." + i;
    private static string AttackAddress(int i) => "10.1.1." + i;

    private sealed class MovableClock(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan by) => _utcNow += by;
    }

    private static SignInProtection BuildProtection(
        ServiceProvider provider,
        IEmailSender? emailSender = null,
        TimeProvider? timeProvider = null,
        SignInThrottleOptions? throttleOptions = null,
        IOutboundEmailQueue? queue = null)
    {
        var db = provider.GetRequiredService<AppDbContext>();
        var clock = timeProvider ?? TimeProvider.System;
        var options = throttleOptions ?? TestThrottleOptions;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["APP_BASE_URL"] = "https://test.apexracers.gg",
        }).Build();
        return new SignInProtection(
            provider.GetRequiredService<UserManager<ApplicationUser>>(), config,
            new SignInThrottleStore(db, clock, options, NullLogger<SignInThrottleStore>.Instance),
            new KnownDeviceStore(db, clock, options),
            queue ?? new ImmediateEmailQueue(emailSender ?? new FakeEmailSender()),
            NullLogger<SignInProtection>.Instance);
    }

    private static async Task CreateUserAsync(
        ServiceProvider provider, string email, string password, bool confirmed, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email, DisplayName = "Driver",
            EmailConfirmed = confirmed,
        };
        Assert.True((await provider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, password)).Succeeded);
    }

    private static async Task<ProtectedSignIn> SeedAndSignInAsync(
        ServiceProvider provider, SignInProtection protection, string email, string password, CancellationToken ct)
    {
        await CreateUserAsync(provider, email, password, true, ct);
        return Assert.IsType<ProtectedSignIn>(await protection.AuthenticateAsync(new LoginRequest(email, password), null, null, ct));
    }

    private static async Task ConfirmUserAsync(ServiceProvider provider, string email)
    {
        var manager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await manager.FindByEmailAsync(email))!;
        user.EmailConfirmed = true;
        Assert.True((await manager.UpdateAsync(user)).Succeeded);
    }

    private static async Task<string> RememberedDeviceAsync(
        SignInProtection protection, string email, string password, CancellationToken ct)
    {
        var result = Assert.IsType<ProtectedSignIn>(await protection.AuthenticateAsync(new LoginRequest(email, password), Owner, null, ct));
        return Assert.IsType<IssuedDevice>(result.Device).Token;
    }

    [Fact]
    public async Task AuthenticateAsync_ExhaustedAddress_IsRefusedEvenWithTheCorrectPassword()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "locked@example.com", "Pass1234", ct);

        // PerAddressMaxFailures = 3 in TestThrottleOptions.
        for (var i = 0; i < 3; i++)
            await svc.AuthenticateAsync(new LoginRequest("locked@example.com", "WrongPassword"), Guesser, null, ct);

        // Checked before the password, so finding it during the window still does not get in.
        Assert.Null(await svc.AuthenticateAsync(new LoginRequest("locked@example.com", "Pass1234"), Guesser, null, ct));
    }

    /// <summary>
    /// The whole of issue #300. An account-wide counter let five requests from a stranger lock a
    /// Driver out of their own account; the allowance is now per source address, so exhausting one
    /// says nothing about any other.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_AddressExhaustedByAGuesser_DoesNotLockTheOwnerOut()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "victim@example.com", "Pass1234", ct);

        // Far past the allowance, all from one stranger.
        for (var i = 0; i < 10; i++)
            await svc.AuthenticateAsync(new LoginRequest("victim@example.com", "WrongPassword"), Guesser, null, ct);

        // That address is spent...
        Assert.Null(await svc.AuthenticateAsync(new LoginRequest("victim@example.com", "Pass1234"), Guesser, null, ct));

        // ...and the owner, on their own machine, is completely unaffected.
        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("victim@example.com", "Pass1234"), Owner, null, ct));
    }

    /// <summary>
    /// The whole of issue #314, and the case issue #300's per-address scope could not reach: the
    /// guesser is on the <em>same</em> address as the Driver — a carrier-grade NAT pool, an
    /// employer's egress, a shared VPN exit — so exhausting "their own" allowance exhausts the
    /// Driver's too. A browser the Driver has already signed in from is the one thing the attacker
    /// next to them does not have.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_SharedAddressExhaustedByAGuesser_StillAdmitsAKnownDevice()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        const string Shared = "203.0.113.99";
        await SeedAndSignInAsync(provider, svc, "nat@example.com", "Pass1234", ct);

        // The Driver's browser becomes known by signing in — the cookie the response would carry is
        // minted by the sign-in itself, so this is the real path rather than a test-only shortcut.
        var device = await RememberedDeviceAsync(svc, "nat@example.com", "Pass1234", ct);

        // The guesser, sharing that address, burns it far past the allowance.
        for (var i = 0; i < 10; i++)
            await svc.AuthenticateAsync(new LoginRequest("nat@example.com", "WrongPassword"), Shared, null, ct);

        // The address is spent — a Driver on a new browser is denied from that network, which is the
        // residual this design accepts and bounds.
        Assert.Null(await svc.AuthenticateAsync(new LoginRequest("nat@example.com", "Pass1234"), Shared, null, ct));

        // But the Driver's own browser, from that very same address, still gets in.
        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("nat@example.com", "Pass1234"), Shared, device, ct));
    }

    /// <summary>
    /// The exemption is an allowance, never a bypass — otherwise a stolen cookie would buy unlimited
    /// guesses against an account whose password is still unknown. A device that has spent its own
    /// window is thrown back on the address scope, so exhausting both is what it takes to be refused.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_KnownDeviceThatExhaustsDeviceAndAddress_IsRefused()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "device@example.com", "Pass1234", ct);
        var device = await RememberedDeviceAsync(svc, "device@example.com", "Pass1234", ct);

        // PerAddressMaxFailures = 3 in TestThrottleOptions. The first three spend the device, the
        // next three spend that address, and only then is there nowhere left to go.
        for (var i = 0; i < 6; i++)
            await svc.AuthenticateAsync(new LoginRequest("device@example.com", "WrongPassword"), Guesser, device, ct);

        Assert.Null(await svc.AuthenticateAsync(new LoginRequest("device@example.com", "Pass1234"), Guesser, device, ct));
    }

    /// <summary>
    /// The invariant issue #300 established — the throttle must never deny the account's owner — and
    /// the way this exemption could have broken it. Someone who copies a cookie can pin that device's
    /// counter from anywhere; the Driver's own browser presents the very same cookie. Had an
    /// exhausted device simply been refused, that would have denied the Driver with the correct
    /// password from any network, which is #300's shape rebuilt on a new key and far cheaper to
    /// reach than the shared egress this work set out to fix.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_DeviceCounterPinnedByACopiedCookie_DoesNotLockTheOwnerOut()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "pinned@example.com", "Pass1234", ct);
        var device = await RememberedDeviceAsync(svc, "pinned@example.com", "Pass1234", ct);

        // The thief, from their own address, spends the device's whole window and then some.
        for (var i = 0; i < 8; i++)
            await svc.AuthenticateAsync(new LoginRequest("pinned@example.com", "WrongPassword"), Guesser, device, ct);

        // The Driver, on that same browser but their own address, still gets in: a device may only
        // ever add allowance, never remove the route they would have had without one.
        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("pinned@example.com", "Pass1234"), Owner, device, ct));
    }

    /// <summary>
    /// The pinning attack heals after one correct password. A thief leaves the device counter spent;
    /// the Driver gets in through the address, and that success must clear the device even though it
    /// was not the scope that admitted them — otherwise the exemption stays switched off for the
    /// rest of the window, which is exactly while the attack is running.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_SuccessAfterAFallThrough_ClearsTheDeviceSoTheExemptionReturns()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        const string Shared = "203.0.113.55";
        await SeedAndSignInAsync(provider, svc, "heal@example.com", "Pass1234", ct);
        var device = await RememberedDeviceAsync(svc, "heal@example.com", "Pass1234", ct);

        // The thief spends the device's window from their own address.
        for (var i = 0; i < 4; i++)
            await svc.AuthenticateAsync(new LoginRequest("heal@example.com", "WrongPassword"), Guesser, device, ct);

        // The Driver gets in via their own clean address, which heals the device.
        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("heal@example.com", "Pass1234"), Owner, device, ct));

        // Proof it healed: a guesser now spends the shared address, and the Driver still gets in on
        // the device — which would be impossible if the device were still spent.
        for (var i = 0; i < 4; i++)
            await svc.AuthenticateAsync(new LoginRequest("heal@example.com", "WrongPassword"), Shared, null, ct);

        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("heal@example.com", "Pass1234"), Shared, device, ct));
    }

    /// <summary>
    /// The exemption must not widen the total allowance. A cookie holder is charged against the
    /// device and the address together, so dropping the cookie part way through buys no extra
    /// attempts — without that, the real ceiling would be the sum of both windows.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_SpendingTheDeviceWindow_AlsoSpendsThatAddress()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "additive@example.com", "Pass1234", ct);
        var device = await RememberedDeviceAsync(svc, "additive@example.com", "Pass1234", ct);

        // Three failures with the cookie: PerAddressMaxFailures = 3, so this spends the device.
        for (var i = 0; i < 3; i++)
            await svc.AuthenticateAsync(new LoginRequest("additive@example.com", "WrongPassword"), Guesser, device, ct);

        // Dropping the cookie yields nothing, because that address has been charged all along.
        Assert.Null(await svc.AuthenticateAsync(new LoginRequest("additive@example.com", "Pass1234"), Guesser, null, ct));
    }

    /// <summary>
    /// A device is recognised without reference to the account the caller named, so ownership has to
    /// be checked afterwards — otherwise any Driver's cookie would exempt a guesser on every other
    /// account.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_DeviceBelongingToAnotherAccount_GetsNoExemption()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        const string Shared = "203.0.113.77";
        await SeedAndSignInAsync(provider, svc, "target@example.com", "Pass1234", ct);
        await SeedAndSignInAsync(provider, svc, "attacker@example.com", "Pass1234", ct);

        // The attacker holds a perfectly valid cookie — for their own account.
        var theirDevice = await RememberedDeviceAsync(svc, "attacker@example.com", "Pass1234", ct);

        for (var i = 0; i < 3; i++)
            await svc.AuthenticateAsync(new LoginRequest("target@example.com", "WrongPassword"), Shared, theirDevice, ct);

        // It bought them nothing: they were throttled on the address like anyone else.
        Assert.Null(await svc.AuthenticateAsync(new LoginRequest("target@example.com", "Pass1234"), Shared, theirDevice, ct));
    }

    /// <summary>
    /// A cookie nobody recognises must be inert rather than an error — a Driver whose device record
    /// has been purged or has expired simply falls back to the address scope.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_UnrecognisedDeviceValue_FallsBackToTheAddressScope()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "stale@example.com", "Pass1234", ct);

        Assert.NotNull(await svc.AuthenticateAsync(
            new LoginRequest("stale@example.com", "Pass1234"), Owner, "not-a-real-device", ct));

        for (var i = 0; i < 3; i++)
            await svc.AuthenticateAsync(new LoginRequest("stale@example.com", "WrongPassword"), Guesser, "not-a-real-device", ct);

        Assert.Null(await svc.AuthenticateAsync(
            new LoginRequest("stale@example.com", "Pass1234"), Guesser, "not-a-real-device", ct));
    }

    /// <summary>
    /// A successful sign-in clears the device's own counter, not the address's — the same rule the
    /// address path follows, applied to the scope that was actually used.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_SuccessOnAKnownDevice_ClearsThatDevicesFailures()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "clear@example.com", "Pass1234", ct);
        var device = await RememberedDeviceAsync(svc, "clear@example.com", "Pass1234", ct);

        // Two failures, then the right password, then two more: without the clear the second pair
        // would exhaust the allowance and the final sign-in would be refused.
        for (var i = 0; i < 2; i++)
            await svc.AuthenticateAsync(new LoginRequest("clear@example.com", "WrongPassword"), Owner, device, ct);

        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("clear@example.com", "Pass1234"), Owner, device, ct));

        for (var i = 0; i < 2; i++)
            await svc.AuthenticateAsync(new LoginRequest("clear@example.com", "WrongPassword"), Owner, device, ct);

        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("clear@example.com", "Pass1234"), Owner, device, ct));
    }

    [Fact]
    public async Task AuthenticateAsync_ExhaustedAddress_IsIndistinguishableFromAnUnknownEmail()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "locked@example.com", "Pass1234", ct);
        for (var i = 0; i < 3; i++)
            await svc.AuthenticateAsync(new LoginRequest("locked@example.com", "WrongPassword"), Guesser, null, ct);

        // Only a real account can be throttled, so any answer unique to that state names one. Both
        // the right and the wrong password must read like an address that was never registered — the
        // right one especially, or a guesser would learn from the response that they had found it.
        var rightPassword = await svc.AuthenticateAsync(new LoginRequest("locked@example.com", "Pass1234"), Guesser, null, ct);
        var wrongPassword = await svc.AuthenticateAsync(new LoginRequest("locked@example.com", "StillWrong"), Guesser, null, ct);
        var unknown = await svc.AuthenticateAsync(new LoginRequest("nobody@example.com", "Pass1234"), Guesser, null, ct);

        Assert.Equal(unknown, rightPassword);
        Assert.Equal(unknown, wrongPassword);
    }

    [Fact]
    public async Task AuthenticateAsync_RepeatedFailures_EmailTheOwnerOnlyOnce()
    {
        await using var provider = BuildProvider();
        var emails = new FakeEmailSender();
        var svc = BuildProtection(provider, emails);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "locked@example.com", "Pass1234", ct);
        emails.Clear();

        for (var i = 0; i < 3; i++)
            await svc.AuthenticateAsync(new LoginRequest("locked@example.com", "WrongPassword"), Guesser, null, ct);

        // Email is the only channel that reaches the owner rather than whoever is guessing, so it is
        // the only place this may be disclosed.
        var sent = Assert.Single(emails.Sent);
        Assert.Equal("locked@example.com", sent.To);
        Assert.Contains("Failed sign-in attempts", sent.Subject);
        // It must not claim the account is locked: since #300 it is not, and the owner can still
        // sign in from their own machine.
        Assert.DoesNotContain("locked", sent.Subject, StringComparison.OrdinalIgnoreCase);

        // Sign-in is unauthenticated, so an email per failure would be an inbox a stranger could
        // fill. Keep hammering from the same address and from fresh ones; neither earns a second.
        for (var i = 0; i < 5; i++)
            await svc.AuthenticateAsync(new LoginRequest("locked@example.com", "WrongPassword"), Guesser, null, ct);
        for (var i = 0; i < 5; i++)
            await svc.AuthenticateAsync(new LoginRequest("locked@example.com", "WrongPassword"), FreshAddress(i), null, ct);

        Assert.Single(emails.Sent);
    }

    [Fact]
    public async Task AuthenticateAsync_SuccessfulSignIn_ClearsOnlyItsOwnAddress()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "shared@example.com", "Pass1234", ct);

        for (var i = 0; i < 3; i++)
            await svc.AuthenticateAsync(new LoginRequest("shared@example.com", "WrongPassword"), Guesser, null, ct);

        // The owner signing in is not evidence the guesser stopped, so it must not hand them a
        // fresh allowance.
        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("shared@example.com", "Pass1234"), Owner, null, ct));
        Assert.Null(await svc.AuthenticateAsync(new LoginRequest("shared@example.com", "Pass1234"), Guesser, null, ct));
    }

    /// <summary>
    /// Crossing the account-wide high-water mark shrinks every address's allowance — but it must
    /// never deny an address that has not itself failed, or the denial of service is back.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_AccountUnderDistributedAttack_TightensOthersButStillAdmitsACleanAddress()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(
            provider,
            throttleOptions: SignInThrottle.Defaults with
            {
                PerAddressMaxFailures = 3,
                AccountHighWaterFailures = 6,
            });
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "target@example.com", "Pass1234", ct);

        // Six failures spread over three addresses: each stays under its own allowance of three,
        // so the account-wide mark is what trips.
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 2; j++)
                await svc.AuthenticateAsync(new LoginRequest("target@example.com", "WrongPassword"), AttackAddress(i), null, ct);

        // A fresh address now gets one failure instead of three...
        Assert.Null(await svc.AuthenticateAsync(new LoginRequest("target@example.com", "WrongPassword"), "10.9.9.9", null, ct));
        Assert.Null(await svc.AuthenticateAsync(new LoginRequest("target@example.com", "Pass1234"), "10.9.9.9", null, ct));

        // ...while the owner, who has failed nothing, still gets in. This is the property that keeps
        // the tightened state from becoming a denial of service.
        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("target@example.com", "Pass1234"), Owner, null, ct));
    }

    /// <summary>
    /// A refused attempt is not recorded, so knocking on a closed door cannot hold it closed. This is
    /// the property that stopped the old account-wide lockout from being extendable for ever.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_AttemptsWhileRefused_DoNotExtendTheWindow()
    {
        await using var provider = BuildProvider();
        var clock = new MovableClock(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
        var svc = BuildProtection(provider, timeProvider: clock);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "patient@example.com", "Pass1234", ct);

        for (var i = 0; i < 3; i++)
            await svc.AuthenticateAsync(new LoginRequest("patient@example.com", "WrongPassword"), Guesser, null, ct);

        // Keep knocking throughout the window. None of these may push the window out.
        for (var i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await svc.AuthenticateAsync(new LoginRequest("patient@example.com", "WrongPassword"), Guesser, null, ct);
        }

        // Past the original window's end, measured from the failure that opened it.
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("patient@example.com", "Pass1234"), Guesser, null, ct));
    }

    [Fact]
    public async Task AuthenticateAsync_SuccessAfterFailures_ResetsFailedCount()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "reset@example.com", "Pass1234", ct);

        // Two failures (below the threshold of 3), then a success that resets the counter.
        await svc.AuthenticateAsync(new LoginRequest("reset@example.com", "nope"), null, null, ct);
        await svc.AuthenticateAsync(new LoginRequest("reset@example.com", "nope"), null, null, ct);
        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("reset@example.com", "Pass1234"), null, null, ct));

        // Two more failures would have locked the account if the counter had not reset, so the
        // correct password must still work afterwards.
        await svc.AuthenticateAsync(new LoginRequest("reset@example.com", "nope"), null, null, ct);
        await svc.AuthenticateAsync(new LoginRequest("reset@example.com", "nope"), null, null, ct);

        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("reset@example.com", "Pass1234"), null, null, ct));
    }

    [Fact]
    public async Task AuthenticateAsync_UnconfirmedAccount_IsIndistinguishableFromAnUnknownEmail()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);

        await CreateUserAsync(provider, "pending@example.com", "Pass1234", false, TestContext.Current.CancellationToken);

        // This is what actually closes the registration oracle. If the credentials an attacker just
        // submitted worked here, success would tell them the address had been free and failure would
        // tell them it was taken — exactly the answer registration withholds.
        var unconfirmed = await svc.AuthenticateAsync(new LoginRequest("pending@example.com", "Pass1234"), null, null, TestContext.Current.CancellationToken);
        var unknown = await svc.AuthenticateAsync(new LoginRequest("nobody@example.com", "Pass1234"), null, null, TestContext.Current.CancellationToken);

        Assert.Equal(unknown, unconfirmed);
        Assert.Null(unconfirmed);
    }

    [Fact]
    public async Task AuthenticateAsync_UnconfirmedAccount_CannotBeLockedOutByAStranger()
    {
        await using var provider = BuildProvider();
        var svc = BuildProtection(provider);

        await CreateUserAsync(provider, "pending@example.com", "Pass1234", false, TestContext.Current.CancellationToken);

        // The confirmation check runs ahead of the failure counter, so hammering an address that has
        // not been confirmed cannot burn its lockout budget — otherwise a stranger could keep the
        // real owner locked out from the moment they signed up.
        for (var i = 0; i < 5; i++)
            await svc.AuthenticateAsync(new LoginRequest("pending@example.com", "WrongPassword"), null, null, TestContext.Current.CancellationToken);

        await ConfirmUserAsync(provider, "pending@example.com");

        Assert.NotNull(await svc.AuthenticateAsync(new LoginRequest("pending@example.com", "Pass1234"), null, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuthenticateAsync_LockedAccount_StillDoesThePasswordWork()
    {
        var hasher = new ObservedPasswordHasher();
        await using var provider = BuildProvider(passwordHasher: hasher);
        var svc = BuildProtection(provider);
        var ct = TestContext.Current.CancellationToken;

        await SeedAndSignInAsync(provider, svc, "locked@example.com", "Pass1234", ct);
        for (var i = 0; i < 3; i++)
            await svc.AuthenticateAsync(new LoginRequest("locked@example.com", "WrongPassword"), null, null, ct);

        // The lockout returns before the stored hash is consulted, so without the stand-in it would
        // be the fastest path of all — and a locked account is one that certainly exists.
        var before = (hasher.RealVerifications + hasher.StandInVerifications);
        Assert.Null(await svc.AuthenticateAsync(new LoginRequest("locked@example.com", "Pass1234"), null, null, ct));

        Assert.Equal(1, (hasher.RealVerifications + hasher.StandInVerifications) - before);
    }

    private sealed class ObservedPasswordHasher : IPasswordHasher<ApplicationUser>
    {
        private PasswordHasher<ApplicationUser> Inner { get; } = new();
        private int _real;
        private int _standIn;
        public int RealVerifications => _real;
        public int StandInVerifications => _standIn;
        public string HashPassword(ApplicationUser user, string password) => Inner.HashPassword(user, password);
        public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hash, string password)
        {
            if (user.Id == Guid.Empty) Interlocked.Increment(ref _standIn);
            else Interlocked.Increment(ref _real);
            return Inner.VerifyHashedPassword(user, hash, password);
        }
    }

    private sealed class RecordingQueue : IOutboundEmailQueue
    {
        public System.Collections.Concurrent.ConcurrentQueue<OutboundEmail> Messages { get; } = new();
        public void Enqueue(OutboundEmail email) => Messages.Enqueue(email);
    }

    private sealed class ThrowingQueue : IOutboundEmailQueue
    {
        public void Enqueue(OutboundEmail email) => throw new InvalidOperationException("queue unavailable");
    }

    private sealed class DeviceQueries : DbCommandInterceptor
    {
        public List<string> Reads { get; } = [];
        public Func<CancellationToken, Task>? BeforeClaim { get; set; }
        public Exception? ClearFailure { get; set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("KnownDevices")) Reads.Add("device");
            else if (command.CommandText.Contains("identity.\"Users\"")) Reads.Add("user");
            if (BeforeClaim is { } action && command.CommandText.StartsWith("UPDATE identity.\"KnownDevices\""))
            {
                BeforeClaim = null;
                await action(cancellationToken);
            }
            return result;
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (ClearFailure is { } failure && command.CommandText.Contains("KnownDevices") &&
                command.CommandText.Contains("\"FailureCount\"")) throw failure;
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData("missing@example.com", false)]
    [InlineData("pending@example.com", false)]
    [InlineData("confirmed@example.com", true)]
    public async Task AuthenticateAsync_RecognisesPresentedCookieBeforeLookingUpAnyAccount(string email, bool confirmed)
    {
        var ct = TestContext.Current.CancellationToken;
        var queries = new DeviceQueries();
        await using var provider = BuildProvider(postgres.CreateOptions(queries));
        if (email != "missing@example.com") await CreateUserAsync(provider, email, "Pass1234", confirmed, ct);
        queries.Reads.Clear();
        await BuildProtection(provider).AuthenticateAsync(new LoginRequest(email, "WrongPassword"), Owner, "presented-cookie", ct);
        Assert.Equal(new[] { "device", "user" }, queries.Reads.Take(2));
    }

    [Theory]
    [InlineData("missing@example.com", "Pass1234", false)]
    [InlineData("pending@example.com", "Pass1234", false)]
    [InlineData("confirmed@example.com", "WrongPassword", true)]
    public async Task AuthenticateAsync_RefusalsPerformOnePasswordVerification(string email, string password, bool confirmed)
    {
        var ct = TestContext.Current.CancellationToken;
        var hasher = new ObservedPasswordHasher();
        await using var provider = BuildProvider(passwordHasher: hasher);
        if (email != "missing@example.com") await CreateUserAsync(provider, email, "Pass1234", confirmed, ct);
        Assert.Null(await BuildProtection(provider).AuthenticateAsync(new LoginRequest(email, password), Owner, null, ct));
        Assert.Equal(1, hasher.RealVerifications + hasher.StandInVerifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticateAsync_DeviceDisappearingAfterRecognitionFallsBackToAddress(bool addressSpent)
    {
        var ct = TestContext.Current.CancellationToken;
        var queries = new DeviceQueries();
        var options = postgres.CreateOptions(queries);
        await using var provider = BuildProvider(options);
        var protection = BuildProtection(provider);
        var initial = await SeedAndSignInAsync(provider, protection, "vanished@example.com", "Pass1234", ct);
        if (addressSpent)
            for (var i = 0; i < 3; i++) await protection.AuthenticateAsync(new LoginRequest("vanished@example.com", "WrongPassword"), Guesser, null, ct);
        queries.BeforeClaim = async token =>
        {
            await using var plain = new AppDbContext(postgres.WithInterceptors(options));
            await plain.KnownDevices.ExecuteDeleteAsync(token);
        };
        var result = await protection.AuthenticateAsync(new LoginRequest("vanished@example.com", "Pass1234"), Guesser, initial.Device!.Token, ct);
        if (addressSpent) Assert.Null(result);
        else Assert.NotNull(result);
    }

    [Fact]
    public async Task AuthenticateAsync_ConcurrentAttemptsAdmitOnlyTheAllowanceAndQueueOneNotice()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = postgres.CreateOptions();
        var hasher = new ObservedPasswordHasher();
        var queue = new RecordingQueue();
        await using (var setup = BuildProvider(options))
            await CreateUserAsync(setup, "burst@example.com", "Pass1234", true, ct);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var provider = BuildProvider(options, hasher);
            return await BuildProtection(provider, queue: queue).AuthenticateAsync(new LoginRequest("burst@example.com", "WrongPassword"), Guesser, null, ct);
        }));
        Assert.All(results, result => Assert.Null(result));
        Assert.Equal(3, hasher.RealVerifications);
        Assert.Equal(9, hasher.StandInVerifications);
        Assert.Single(queue.Messages);
        await using var owner = BuildProvider(options);
        Assert.Null(await BuildProtection(owner).AuthenticateAsync(new LoginRequest("burst@example.com", "Pass1234"), Guesser, null, ct));
    }

    [Fact]
    public async Task AuthenticateAsync_QueueFailureCannotChangeGenericRefusal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var protection = BuildProtection(provider, queue: new ThrowingQueue());
        await CreateUserAsync(provider, "notice@example.com", "Pass1234", true, ct);
        for (var i = 0; i < 4; i++) Assert.Null(await protection.AuthenticateAsync(new LoginRequest("notice@example.com", "WrongPassword"), Guesser, null, ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticateAsync_DeviceClearFailureIsNonfatalButCancellationPropagates(bool cancellation)
    {
        var ct = TestContext.Current.CancellationToken;
        var queries = new DeviceQueries();
        await using var provider = BuildProvider(postgres.CreateOptions(queries));
        var protection = BuildProtection(provider);
        var initial = await SeedAndSignInAsync(provider, protection, "clear-fault@example.com", "Pass1234", ct);
        queries.ClearFailure = cancellation ? new OperationCanceledException(ct) : new InvalidOperationException("device unavailable");
        var attempt = () => protection.AuthenticateAsync(new LoginRequest("clear-fault@example.com", "Pass1234"), Owner, initial.Device!.Token, ct);
        if (cancellation) await Assert.ThrowsAsync<OperationCanceledException>(attempt);
        else Assert.NotNull(await attempt());
    }

    [Fact]
    public async Task AuthenticateAsync_DeviceSuccessDoesNotResetTheSharedAddress()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var protection = BuildProtection(provider);
        var initial = await SeedAndSignInAsync(provider, protection, "shared-reset@example.com", "Pass1234", ct);
        for (var i = 0; i < 3; i++) await protection.AuthenticateAsync(new LoginRequest("shared-reset@example.com", "WrongPassword"), Guesser, null, ct);
        Assert.NotNull(await protection.AuthenticateAsync(new LoginRequest("shared-reset@example.com", "Pass1234"), Guesser, initial.Device!.Token, ct));
        Assert.Null(await protection.AuthenticateAsync(new LoginRequest("shared-reset@example.com", "Pass1234"), Guesser, null, ct));
    }

    [Fact]
    public async Task AuthenticateAsync_DeviceWindowExpiresAtOriginalBoundaryDespiteRefusedAttempts()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new MovableClock(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        await using var provider = BuildProvider();
        var protection = BuildProtection(provider, timeProvider: clock);
        var initial = await SeedAndSignInAsync(provider, protection, "window@example.com", "Pass1234", ct);
        for (var i = 0; i < 3; i++) await protection.AuthenticateAsync(new LoginRequest("window@example.com", "WrongPassword"), Guesser, initial.Device!.Token, ct);
        for (var i = 0; i < 14; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Null(await protection.AuthenticateAsync(new LoginRequest("window@example.com", "Pass1234"), Guesser, initial.Device!.Token, ct));
        }
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.NotNull(await protection.AuthenticateAsync(new LoginRequest("window@example.com", "Pass1234"), Guesser, initial.Device!.Token, ct));
    }
    [Fact]
    public async Task AuthenticateAsync_SpentDeviceQueuesAPacedNoticeAcrossFreshAddresses()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new MovableClock(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        var queue = new RecordingQueue();
        await using var provider = BuildProvider();
        var protection = BuildProtection(provider, timeProvider: clock, queue: queue);
        var initial = await SeedAndSignInAsync(provider, protection, "device-notice@example.com", "Pass1234", ct);
        for (var i = 0; i < 3; i++)
        {
            Assert.Null(await protection.AuthenticateAsync(new LoginRequest("device-notice@example.com", "WrongPassword"), FreshAddress(i), initial.Device!.Token, ct));
            Assert.Equal(i == 2 ? 1 : 0, queue.Messages.Count);
        }
        Assert.Equal("device-notice@example.com", Assert.Single(queue.Messages).To);
        clock.Advance(TestThrottleOptions.PerAddressWindow);
        for (var i = 0; i < 3; i++)
            await protection.AuthenticateAsync(new LoginRequest("device-notice@example.com", "WrongPassword"), FreshAddress(i + 3), initial.Device!.Token, ct);
        Assert.Single(queue.Messages);
        clock.Advance(TestThrottleOptions.NoticeInterval - TestThrottleOptions.PerAddressWindow + TimeSpan.FromMilliseconds(1));
        for (var i = 0; i < 3; i++)
            await protection.AuthenticateAsync(new LoginRequest("device-notice@example.com", "WrongPassword"), FreshAddress(i + 6), initial.Device!.Token, ct);
        Assert.Equal(2, queue.Messages.Count);
    }

}
