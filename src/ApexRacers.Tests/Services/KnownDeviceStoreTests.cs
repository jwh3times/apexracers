using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// Known-device recognition and its own throttle counter (issue #314).
/// </summary>
/// <remarks>
/// On PostgreSQL rather than SQLite for the same reason as
/// <see cref="SignInThrottleStoreTests"/>: the claim is an <c>UPDATE … RETURNING</c> written as raw
/// SQL, and every read is a <see cref="DateTimeOffset"/> predicate. SQLite fails both at run time
/// rather than at compile time, so a SQLite run would be green without executing the statements the
/// production path depends on.
/// </remarks>
[Collection(PostgreSqlCollection.Name)]
public class KnownDeviceStoreTests(PostgreSqlFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly SignInThrottleOptions Options = new(
        PerAddressMaxFailures: 3,
        TightenedPerAddressMaxFailures: 1,
        AccountHighWaterFailures: 6,
        PerAddressWindow: TimeSpan.FromMinutes(15),
        AccountWindow: TimeSpan.FromHours(1),
        NoticeInterval: TimeSpan.FromHours(1));

    private sealed class MovableClock(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan by) => _utcNow += by;
    }

    /// <summary>
    /// A fresh database with one user, plus the options that reach <em>that same</em> database —
    /// <see cref="PostgreSqlFixture.CreateOptions"/> creates a new one per call, so a test needing
    /// several contexts has to build them from a single instance.
    /// </summary>
    private async Task<(AppDbContext Db, Guid UserId, DbContextOptions<AppDbContext> Options)> NewDbAsync(
        CancellationToken ct)
    {
        var options = postgres.CreateOptions();
        var db = new AppDbContext(options);
        var userId = await AddUserAsync(db, ct);
        return (db, userId, options);
    }

    private static async Task<Guid> AddUserAsync(AppDbContext db, CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"u{userId:N}@example.com",
            Email = $"u{userId:N}@example.com",
            DisplayName = "Driver",
        });
        await db.SaveChangesAsync(ct);
        return userId;
    }

    // ── Recognition ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-device-token")]
    public async Task Recognise_AbsentOrUnknown_ReturnsNull(string? presented)
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, _, _) = await NewDbAsync(ct);
        var store = new KnownDeviceStore(db, new MovableClock(Now), Options);

        Assert.Null(await store.RecogniseAsync(presented, ct));
    }

    [Fact]
    public async Task Recognise_OverlongValue_IsRejectedWithoutQuerying()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, _, _) = await NewDbAsync(ct);
        var store = new KnownDeviceStore(db, new MovableClock(Now), Options);

        Assert.Null(await store.RecogniseAsync(new string('a', 5000), ct));
    }

    [Fact]
    public async Task Recognise_RememberedDevice_ResolvesToItsOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new KnownDeviceStore(db, new MovableClock(Now), Options);

        var token = (await store.RememberAsync(userId, null, ct)).Token;
        var recognised = await store.RecogniseAsync(token, ct);

        Assert.NotNull(recognised);
        Assert.Equal(userId, recognised.UserId);
        // Carried back so a renewal can hand the same value to the browser.
        Assert.Equal(token, recognised.Token);
    }

    [Fact]
    public async Task Recognise_AfterTheLifetimeElapses_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var clock = new MovableClock(Now);
        var store = new KnownDeviceStore(db, clock, Options);

        var token = (await store.RememberAsync(userId, null, ct)).Token;
        clock.Advance(KnownDeviceStore.DeviceLifetime + TimeSpan.FromMinutes(1));

        Assert.Null(await store.RecogniseAsync(token, ct));
    }

    [Fact]
    public async Task Recognise_DoesNotStoreTheRawValue()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new KnownDeviceStore(db, new MovableClock(Now), Options);

        var token = (await store.RememberAsync(userId, null, ct)).Token;

        // Only the hash is persisted, so a reader of this table cannot present a cookie.
        var stored = await db.KnownDevices.AsNoTracking().SingleAsync(ct);
        Assert.NotEqual(token, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
    }

    // ── The counter ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_ReachingTheAllowance_RefusesFurtherAttempts()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new KnownDeviceStore(db, new MovableClock(Now), Options);
        var device = await RememberedDeviceAsync(store, userId, ct);

        // Three is the allowance, so the third claim is the last permitted one.
        for (var i = 0; i < 3; i++)
            Assert.False((await store.ClaimAttemptAsync(device.Id, userId, ct))!.Value.Refused);

        Assert.True((await store.ClaimAttemptAsync(device.Id, userId, ct))!.Value.Refused);
    }

    [Fact]
    public async Task Claim_WithinTheWindow_DoesNotMoveTheWindowStart()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var clock = new MovableClock(Now);
        var store = new KnownDeviceStore(db, clock, Options);
        var device = await RememberedDeviceAsync(store, userId, ct);

        await store.ClaimAttemptAsync(device.Id, userId, ct);
        var opened = (await db.KnownDevices.AsNoTracking().SingleAsync(ct)).WindowStartedAt;

        clock.Advance(TimeSpan.FromMinutes(5));
        await store.ClaimAttemptAsync(device.Id, userId, ct);

        // A window that restarted on every failure could be held open indefinitely.
        Assert.Equal(opened, (await db.KnownDevices.AsNoTracking().SingleAsync(ct)).WindowStartedAt);
    }

    [Fact]
    public async Task Claim_AfterTheWindowElapses_StartsAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var clock = new MovableClock(Now);
        var store = new KnownDeviceStore(db, clock, Options);
        var device = await RememberedDeviceAsync(store, userId, ct);

        for (var i = 0; i < 4; i++)
            await store.ClaimAttemptAsync(device.Id, userId, ct);
        Assert.True((await store.ClaimAttemptAsync(device.Id, userId, ct))!.Value.Refused);

        clock.Advance(Options.PerAddressWindow + TimeSpan.FromMinutes(1));

        Assert.False((await store.ClaimAttemptAsync(device.Id, userId, ct))!.Value.Refused);
    }

    [Fact]
    public async Task Claim_KeepsTheOrdinaryAllowanceWhileTheAccountIsUnderAttack()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new KnownDeviceStore(db, new MovableClock(Now), Options);
        var device = await RememberedDeviceAsync(store, userId, ct);

        db.SignInAccountFailures.Add(new SignInAccountFailure
        {
            UserId = userId,
            FailureCount = Options.AccountHighWaterFailures,
            WindowStartedAt = Now,
            LastFailureAt = Now,
        });
        await db.SaveChangesAsync(ct);

        var decision = (await store.ClaimAttemptAsync(device.Id, userId, ct))!.Value;

        // The account is under attack, but a recognised device is not tightened — that is the whole
        // exemption. It is still reported, because the owner's notice reads it.
        Assert.True(decision.UnderAttack);
        Assert.False(decision.Refused);
        // The tightened allowance is 1, so a device that was being tightened would already be spent.
        Assert.False(decision.Spent);
    }

    [Fact]
    public async Task Claim_OnADeviceThatHasVanished_ReportsAbsenceRatherThanRefusing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new KnownDeviceStore(db, new MovableClock(Now), Options);
        var device = await RememberedDeviceAsync(store, userId, ct);

        await db.KnownDevices.Where(d => d.Id == device.Id).ExecuteDeleteAsync(ct);

        // A purge between recognition and the claim must not read as "exhausted" — that would deny
        // the Driver. Absence is its own answer and the caller falls back to the address scope.
        Assert.Null(await store.ClaimAttemptAsync(device.Id, userId, ct));
    }

    /// <summary>
    /// Ownership is enforced by the statement, not only by the in-memory check at the call site, so
    /// no caller can increment another account's device by passing a mismatched pair.
    /// </summary>
    [Fact]
    public async Task Claim_WithTheWrongOwner_TouchesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var otherId = await AddUserAsync(db, ct);
        var store = new KnownDeviceStore(db, new MovableClock(Now), Options);
        var device = await RememberedDeviceAsync(store, userId, ct);

        Assert.Null(await store.ClaimAttemptAsync(device.Id, otherId, ct));
        Assert.Equal(0, (await db.KnownDevices.AsNoTracking().SingleAsync(ct)).FailureCount);
    }

    /// <summary>
    /// <c>Spent</c> is distinct from <c>Refused</c>: the attempt landing exactly on the allowance is
    /// permitted and uses it up, and that is the moment the owner should be told.
    /// </summary>
    [Fact]
    public async Task Claim_LandingExactlyOnTheAllowance_IsPermittedButReportsSpent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new KnownDeviceStore(db, new MovableClock(Now), Options);
        var device = await RememberedDeviceAsync(store, userId, ct);

        for (var i = 0; i < Options.PerAddressMaxFailures - 1; i++)
        {
            var early = (await store.ClaimAttemptAsync(device.Id, userId, ct))!.Value;
            Assert.False(early.Refused);
            Assert.False(early.Spent);
        }

        var last = (await store.ClaimAttemptAsync(device.Id, userId, ct))!.Value;
        Assert.False(last.Refused);
        Assert.True(last.Spent);
    }

    [Fact]
    public async Task ForgetAll_RemovesEveryDeviceForThatAccountOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var otherId = await AddUserAsync(db, ct);
        var clock = new MovableClock(Now);
        var store = new KnownDeviceStore(db, clock, Options);

        var mine = (await store.RememberAsync(userId, null, ct)).Token;
        clock.Advance(TimeSpan.FromMinutes(1));
        var alsoMine = (await store.RememberAsync(userId, null, ct)).Token;
        var theirs = (await store.RememberAsync(otherId, null, ct)).Token;

        Assert.Equal(2, await store.ForgetAllAsync(userId, ct));

        // Both of this account's devices are gone — a password reset must not leave one behind.
        Assert.Null(await store.RecogniseAsync(mine, ct));
        Assert.Null(await store.RecogniseAsync(alsoMine, ct));
        Assert.NotNull(await store.RecogniseAsync(theirs, ct));
    }

    [Fact]
    public async Task ClearFailures_AfterASuccess_RestoresTheAllowance()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new KnownDeviceStore(db, new MovableClock(Now), Options);
        var device = await RememberedDeviceAsync(store, userId, ct);

        for (var i = 0; i < 4; i++)
            await store.ClaimAttemptAsync(device.Id, userId, ct);
        Assert.True((await store.ClaimAttemptAsync(device.Id, userId, ct))!.Value.Refused);

        await store.ClearFailuresAsync(device.Id, ct);

        Assert.False((await store.ClaimAttemptAsync(device.Id, userId, ct))!.Value.Refused);
    }

    /// <summary>
    /// The gate is the write, so concurrent claims must receive distinct counts.
    /// </summary>
    /// <remarks>
    /// Written the way <see cref="SignInThrottleStoreTests"/>' concurrency test is, and for the
    /// reason recorded there: a read-then-write gate lets a burst all read the same count and all
    /// pass, so the allowance silently becomes <c>max(allowance, requests in flight)</c>. The
    /// assertion is on how many were <em>permitted</em>, because a test that only checked the final
    /// count would pass against the broken implementation too.
    /// </remarks>
    [Fact]
    public async Task Claim_ConcurrentAttempts_PermitsOnlyTheAllowance()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, options) = await NewDbAsync(ct);
        var setup = new KnownDeviceStore(db, new MovableClock(Now), Options);
        var device = await RememberedDeviceAsync(setup, userId, ct);

        const int Callers = 12;
        var ready = new SemaphoreSlim(0, Callers);
        var release = new TaskCompletionSource();

        var attempts = Enumerable.Range(0, Callers).Select(async _ =>
        {
            // Each caller needs its own context, but every context must reach the same database.
            await using var ownDb = new AppDbContext(options);
            var store = new KnownDeviceStore(ownDb, new MovableClock(Now), Options);
            ready.Release();
            await release.Task;
            return await store.ClaimAttemptAsync(device.Id, userId, ct);
        }).ToArray();

        for (var i = 0; i < Callers; i++)
            await ready.WaitAsync(ct);
        release.SetResult();

        var decisions = await Task.WhenAll(attempts);

        // Every claim found its row, so none of these is absence rather than a verdict.
        Assert.All(decisions, d => Assert.NotNull(d));
        Assert.Equal(Options.PerAddressMaxFailures, decisions.Count(d => !d!.Value.Refused));
        Assert.Equal(Callers, (await db.KnownDevices.AsNoTracking().SingleAsync(ct)).FailureCount);
    }

    // ── Remembering ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Remember_AnAlreadyKnownDevice_RenewsItInPlace()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var clock = new MovableClock(Now);
        var store = new KnownDeviceStore(db, clock, Options);

        var first = (await store.RememberAsync(userId, null, ct)).Token;
        clock.Advance(TimeSpan.FromDays(30));
        var recognised = await store.RecogniseAsync(first, ct);
        var second = (await store.RememberAsync(userId, recognised, ct)).Token;

        // Same value, same row — a Driver who signs in regularly keeps one device, not one per
        // sign-in — and the expiry moved out.
        Assert.Equal(first, second);
        var row = await db.KnownDevices.AsNoTracking().SingleAsync(ct);
        Assert.Equal(clock.GetUtcNow() + KnownDeviceStore.DeviceLifetime, row.ExpiresAt);
    }

    [Fact]
    public async Task Remember_ADeviceBelongingToAnotherAccount_MintsANewOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var otherId = await AddUserAsync(db, ct);
        var store = new KnownDeviceStore(db, new MovableClock(Now), Options);

        var theirs = (await store.RememberAsync(otherId, null, ct)).Token;
        var recognised = await store.RecogniseAsync(theirs, ct);
        var mine = (await store.RememberAsync(userId, recognised, ct)).Token;

        Assert.NotEqual(theirs, mine);
        Assert.Equal(2, await db.KnownDevices.CountAsync(ct));
    }

    [Fact]
    public async Task Remember_BeyondTheCap_EvictsTheLeastRecentlySeen()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var clock = new MovableClock(Now);
        var store = new KnownDeviceStore(db, clock, Options);

        var first = (await store.RememberAsync(userId, null, ct)).Token;
        for (var i = 0; i < KnownDeviceStore.MaxDevicesPerUser; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await store.RememberAsync(userId, null, ct);
        }

        Assert.Equal(KnownDeviceStore.MaxDevicesPerUser, await db.KnownDevices.CountAsync(ct));
        // The oldest is gone, so an unbounded number of browsers cannot accumulate on one account.
        Assert.Null(await store.RecogniseAsync(first, ct));
    }

    [Fact]
    public async Task Remember_OnlyEverAffectsItsOwnAccount()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var otherId = await AddUserAsync(db, ct);
        var clock = new MovableClock(Now);
        var store = new KnownDeviceStore(db, clock, Options);

        var theirs = (await store.RememberAsync(otherId, null, ct)).Token;
        for (var i = 0; i <= KnownDeviceStore.MaxDevicesPerUser; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await store.RememberAsync(userId, null, ct);
        }

        // Eviction is scoped to the account that overflowed.
        Assert.NotNull(await store.RecogniseAsync(theirs, ct));
    }

    // ── Purge ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Devices age out on their own expiry rather than on a failure window, and with no grace —
    /// recognition has genuinely lapsed at that instant, so a row kept past it would still name a
    /// device the account can be recognised by.
    /// </summary>
    [Fact]
    public async Task PurgeStale_RemovesDevicesOnlyOnceTheyExpire()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new KnownDeviceStore(db, new MovableClock(Now), Options);

        var token = (await store.RememberAsync(userId, null, ct)).Token;

        // Far past any failure window, but well inside the device lifetime.
        var early = await SignInThrottleCleanupService.PurgeStaleAsync(
            db, Options, Now + TimeSpan.FromDays(1), SignInThrottleCleanupService.Grace, ct);
        Assert.Equal(0, early);
        Assert.NotNull(await store.RecogniseAsync(token, ct));

        var removed = await SignInThrottleCleanupService.PurgeStaleAsync(
            db,
            Options,
            Now + KnownDeviceStore.DeviceLifetime + TimeSpan.FromMinutes(1),
            SignInThrottleCleanupService.Grace,
            ct);

        Assert.Equal(1, removed);
        Assert.False(await db.KnownDevices.AnyAsync(d => d.UserId == userId, ct));
    }

    private static async Task<KnownDeviceRecord> RememberedDeviceAsync(
        KnownDeviceStore store, Guid userId, CancellationToken ct)
    {
        var token = (await store.RememberAsync(userId, null, ct)).Token;
        return (await store.RecogniseAsync(token, ct))!;
    }
}
