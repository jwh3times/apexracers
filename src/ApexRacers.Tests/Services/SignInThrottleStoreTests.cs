using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// The persistence half of issue #300's throttle.
/// </summary>
/// <remarks>
/// On PostgreSQL rather than SQLite because every write here is an <c>ExecuteUpdate</c> over
/// <see cref="DateTimeOffset"/> predicates. SQLite fails that translation at run time rather than at
/// compile time, so a SQLite run would report a green suite that never executed the statements the
/// production path depends on.
/// </remarks>
[Collection(PostgreSqlCollection.Name)]
public class SignInThrottleStoreTests(PostgreSqlFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private const string Guesser = "203.0.113.7";
    private const string Owner = "198.51.100.20";

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
    /// A fresh database with one user in it, plus the options that reach <em>that same</em> database.
    /// </summary>
    /// <remarks>
    /// Handing the options back matters: <see cref="PostgreSqlFixture.CreateOptions"/> creates a new
    /// database on every call, so a test that needs several contexts over one database has to build
    /// them all from a single options instance. Calling it per context instead gives each one its own
    /// empty database, and a concurrency test written that way passes while proving nothing.
    /// </remarks>
    private async Task<(AppDbContext Db, Guid UserId, DbContextOptions<AppDbContext> Options)> NewDbAsync(
        CancellationToken ct)
    {
        var options = postgres.CreateOptions();
        var db = new AppDbContext(options);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"u{userId:N}@example.com",
            Email = $"u{userId:N}@example.com",
            DisplayName = "Driver",
        });
        await db.SaveChangesAsync(ct);
        return (db, userId, options);
    }

    [Fact]
    public async Task Evaluate_NoHistory_Allows()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new SignInThrottleStore(db, new MovableClock(Now), Options);

        var decision = await store.EvaluateAsync(userId, Guesser, ct);

        Assert.False(decision.Refused);
        Assert.Equal(3, decision.Allowance);
    }

    [Fact]
    public async Task RecordFailure_ReachingTheAllowance_RefusesOnlyThatAddress()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new SignInThrottleStore(db, new MovableClock(Now), Options);

        for (var i = 0; i < 3; i++)
            await store.RecordFailureAsync(userId, Guesser, ct);

        Assert.True((await store.EvaluateAsync(userId, Guesser, ct)).Refused);
        // The property issue #300 is about.
        Assert.False((await store.EvaluateAsync(userId, Owner, ct)).Refused);
    }

    [Fact]
    public async Task RecordFailure_WithinTheWindow_DoesNotMoveTheWindowStart()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var clock = new MovableClock(Now);
        var store = new SignInThrottleStore(db, clock, Options);

        await store.RecordFailureAsync(userId, Guesser, ct);
        clock.Advance(TimeSpan.FromMinutes(10));
        await store.RecordFailureAsync(userId, Guesser, ct);

        var row = await db.SignInAddressFailures.AsNoTracking()
            .SingleAsync(f => f.UserId == userId && f.IpAddress == Guesser, ct);

        Assert.Equal(2, row.FailureCount);
        Assert.Equal(Now, row.WindowStartedAt);
    }

    [Fact]
    public async Task RecordFailure_AfterTheWindow_ReopensItFromNow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var clock = new MovableClock(Now);
        var store = new SignInThrottleStore(db, clock, Options);

        for (var i = 0; i < 3; i++)
            await store.RecordFailureAsync(userId, Guesser, ct);

        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.False((await store.EvaluateAsync(userId, Guesser, ct)).Refused);

        await store.RecordFailureAsync(userId, Guesser, ct);
        var row = await db.SignInAddressFailures.AsNoTracking()
            .SingleAsync(f => f.UserId == userId && f.IpAddress == Guesser, ct);

        Assert.Equal(1, row.FailureCount);
        Assert.Equal(Now + TimeSpan.FromMinutes(15), row.WindowStartedAt);
    }

    [Fact]
    public async Task RecordFailure_AcrossManyAddresses_TightensTheAllowanceForAll()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new SignInThrottleStore(db, new MovableClock(Now), Options);

        // Six failures over three addresses: each stays under its own allowance of three.
        for (var i = 0; i < 3; i++)
        {
            await store.RecordFailureAsync(userId, $"10.1.1.{i}", ct);
            await store.RecordFailureAsync(userId, $"10.1.1.{i}", ct);
        }

        var fresh = await store.EvaluateAsync(userId, "10.9.9.9", ct);
        Assert.True(fresh.UnderAttack);
        Assert.Equal(1, fresh.Allowance);
        // Still admitted: it has failed nothing.
        Assert.False(fresh.Refused);
    }

    [Fact]
    public async Task ClearAddress_RemovesOnlyThatAddressAndLeavesTheAccountCounter()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new SignInThrottleStore(db, new MovableClock(Now), Options);

        for (var i = 0; i < 3; i++)
            await store.RecordFailureAsync(userId, Guesser, ct);
        await store.RecordFailureAsync(userId, Owner, ct);

        await store.ClearAddressAsync(userId, Owner, ct);

        Assert.False(await db.SignInAddressFailures.AnyAsync(f => f.UserId == userId && f.IpAddress == Owner, ct));
        Assert.True((await store.EvaluateAsync(userId, Guesser, ct)).Refused);

        // The account-wide counter survives: the owner signing in is not evidence the guesser left.
        var account = await db.SignInAccountFailures.AsNoTracking().SingleAsync(f => f.UserId == userId, ct);
        Assert.Equal(4, account.FailureCount);
    }

    [Fact]
    public async Task RecordFailure_NoticeIsClaimedOnceAcrossManyAddresses()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var clock = new MovableClock(Now);
        var store = new SignInThrottleStore(db, clock, Options);

        var notices = 0;
        for (var address = 0; address < 20; address++)
            for (var attempt = 0; attempt < 4; attempt++)
                if (await store.RecordFailureAsync(userId, $"10.2.2.{address}", ct))
                    notices++;

        // An email per lockout would let a stranger flood the owner's inbox from fresh addresses.
        Assert.Equal(1, notices);
    }

    [Fact]
    public async Task RecordFailure_NoticeIsAvailableAgainAfterTheInterval()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var clock = new MovableClock(Now);
        var store = new SignInThrottleStore(db, clock, Options);

        var first = 0;
        for (var i = 0; i < 3; i++)
            if (await store.RecordFailureAsync(userId, Guesser, ct))
                first++;
        Assert.Equal(1, first);

        // Past the interval, not exactly onto it: the predicate is a strict `<`, so a notice
        // becomes available once MORE than the interval has elapsed.
        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));

        var second = 0;
        for (var i = 0; i < 3; i++)
            if (await store.RecordFailureAsync(userId, Guesser, ct))
                second++;

        Assert.Equal(1, second);
    }

    /// <summary>
    /// Waiting out the account window must not reset the notice pacing — otherwise a patient caller
    /// earns a fresh email every hour by simply pausing.
    /// </summary>
    [Fact]
    public async Task RecordFailure_ReopeningTheAccountWindow_DoesNotResetTheNoticePacing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var clock = new MovableClock(Now);
        var store = new SignInThrottleStore(db, clock, Options);

        for (var i = 0; i < 3; i++)
            await store.RecordFailureAsync(userId, Guesser, ct);

        // Past the account window but inside the notice interval.
        clock.Advance(TimeSpan.FromMinutes(50));

        var notices = 0;
        for (var i = 0; i < 3; i++)
            if (await store.RecordFailureAsync(userId, "10.3.3.3", ct))
                notices++;

        Assert.Equal(0, notices);
        var account = await db.SignInAccountFailures.AsNoTracking().SingleAsync(f => f.UserId == userId, ct);
        Assert.Equal(Now, account.NoticeSentAt);
    }

    /// <summary>
    /// Concurrent failures against one account are the attack, not an edge case: every one of them
    /// has to be counted, or a guesser gets attempts for free.
    /// </summary>
    [Fact]
    public async Task RecordFailure_ConcurrentFailures_AreAllCounted()
    {
        var ct = TestContext.Current.CancellationToken;
        var (seedDb, userId, options) = await NewDbAsync(ct);
        await seedDb.DisposeAsync();

        const int concurrent = 12;
        var ready = new SemaphoreSlim(0, concurrent);
        var go = new TaskCompletionSource();
        var arrived = 0;

        // Every context is built from the ONE options instance above, so they all reach the same
        // database. Building them from fresh CreateOptions() calls would give each worker its own
        // empty database and the test would prove nothing.
        var workers = Enumerable.Range(0, concurrent).Select(async _ =>
        {
            await using var db = new AppDbContext(options);
            var store = new SignInThrottleStore(db, new MovableClock(Now), Options);
            Interlocked.Increment(ref arrived);
            ready.Release();
            await go.Task;
            await store.RecordFailureAsync(userId, Guesser, ct);
        }).ToArray();

        for (var i = 0; i < concurrent; i++)
            await ready.WaitAsync(ct);
        go.SetResult();
        await Task.WhenAll(workers);

        // Assert the gate actually held every worker: without this the test could pass having run
        // them one after another, proving nothing about concurrency.
        Assert.Equal(concurrent, arrived);

        await using var verify = new AppDbContext(options);
        var address = await verify.SignInAddressFailures.AsNoTracking()
            .SingleAsync(f => f.UserId == userId && f.IpAddress == Guesser, ct);
        var account = await verify.SignInAccountFailures.AsNoTracking()
            .SingleAsync(f => f.UserId == userId, ct);

        // Exactly one row each — the unique index held — and nothing was lost to a race.
        Assert.Equal(concurrent, address.FailureCount);
        Assert.Equal(concurrent, account.FailureCount);
    }

    [Theory]
    [InlineData(null, SignInThrottleStore.UnknownAddress)]
    [InlineData("", SignInThrottleStore.UnknownAddress)]
    [InlineData("   ", SignInThrottleStore.UnknownAddress)]
    [InlineData("  203.0.113.7  ", "203.0.113.7")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    public void NormaliseAddress_MapsAbsenceToOneSharedBucket(string? input, string expected) =>
        Assert.Equal(expected, SignInThrottleStore.NormaliseAddress(input));

    /// <summary>
    /// An over-long value cannot be stored, and must not be truncated either — truncation would fold
    /// two real addresses into one allowance. It joins the shared unknown bucket instead.
    /// </summary>
    [Fact]
    public void NormaliseAddress_OverLongValue_IsBucketedNotTruncated() =>
        Assert.Equal(
            SignInThrottleStore.UnknownAddress,
            SignInThrottleStore.NormaliseAddress(new string('a', 57)));

    [Fact]
    public async Task PurgeStale_RemovesRowsPastTheirWindowAndGrace()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new SignInThrottleStore(db, new MovableClock(Now), Options);

        await store.RecordFailureAsync(userId, Guesser, ct);

        // Not yet eligible: the grace keeps the sweep clear of the decision path.
        var early = await SignInThrottleCleanupService.PurgeStaleAsync(
            db, Options, Now + TimeSpan.FromMinutes(20), SignInThrottleCleanupService.Grace, ct);
        Assert.Equal(0, early);
        Assert.True(await db.SignInAddressFailures.AnyAsync(f => f.UserId == userId, ct));

        // Past window + grace for both tables, and past the notice interval.
        var removed = await SignInThrottleCleanupService.PurgeStaleAsync(
            db, Options, Now + TimeSpan.FromHours(4), SignInThrottleCleanupService.Grace, ct);

        Assert.Equal(2, removed);
        Assert.False(await db.SignInAddressFailures.AnyAsync(f => f.UserId == userId, ct));
        Assert.False(await db.SignInAccountFailures.AnyAsync(f => f.UserId == userId, ct));
    }

    /// <summary>
    /// An account row also paces the owner's email, so it must outlive its failure window while that
    /// pacing still matters — deleting it early would hand back a free notice.
    /// </summary>
    [Fact]
    public async Task PurgeStale_KeepsAnAccountRowWhoseNoticeIsStillPacing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var clock = new MovableClock(Now);
        var store = new SignInThrottleStore(db, clock, Options);

        for (var i = 0; i < 3; i++)
            await store.RecordFailureAsync(userId, Guesser, ct);

        // Past the account window + grace, but still inside the notice interval.
        var at = Now + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(50);
        await SignInThrottleCleanupService.PurgeStaleAsync(
            db, Options, at, SignInThrottleCleanupService.Grace, ct);

        Assert.True(await db.SignInAccountFailures.AnyAsync(f => f.UserId == userId, ct));
    }

    [Fact]
    public async Task PurgeStale_LeavesLiveRowsAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new SignInThrottleStore(db, new MovableClock(Now), Options);

        for (var i = 0; i < 3; i++)
            await store.RecordFailureAsync(userId, Guesser, ct);

        await SignInThrottleCleanupService.PurgeStaleAsync(
            db, Options, Now + TimeSpan.FromMinutes(1), SignInThrottleCleanupService.Grace, ct);

        // Purging a live row would hand the guesser their allowance back.
        Assert.True((await store.EvaluateAsync(userId, Guesser, ct)).Refused);
    }

    [Fact]
    public async Task DeletingAUser_CascadesToBothThrottleTables()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, userId, _) = await NewDbAsync(ct);
        var store = new SignInThrottleStore(db, new MovableClock(Now), Options);

        await store.RecordFailureAsync(userId, Guesser, ct);

        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);

        Assert.False(await db.SignInAddressFailures.AnyAsync(f => f.UserId == userId, ct));
        Assert.False(await db.SignInAccountFailures.AnyAsync(f => f.UserId == userId, ct));
    }
}
