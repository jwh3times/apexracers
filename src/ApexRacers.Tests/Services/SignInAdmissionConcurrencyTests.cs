using System.Collections.Concurrent;
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
using Npgsql;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public class SignInAdmissionConcurrencyTests(PostgreSqlFixture postgres, ITestOutputHelper output)
{
    private const string Email = "controlled-concurrency@example.com";
    private const string SourceAddress = "203.0.113.45";
    private static readonly SignInThrottleOptions Options = SignInThrottle.Defaults with
    {
        PerAddressMaxFailures = 1,
        TightenedPerAddressMaxFailures = 1,
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class ObservedHasher : IPasswordHasher<ApplicationUser>
    {
        private PasswordHasher<ApplicationUser> Inner { get; } = new();
        private int _actual;
        private int _standIn;
        public int Actual => _actual;
        public int StandIn => _standIn;
        public Action<ApplicationUser>? BeforeActualVerification { get; set; }
        public string HashPassword(ApplicationUser user, string password) => Inner.HashPassword(user, password);
        public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hash, string password)
        {
            if (user.Id == Guid.Empty) Interlocked.Increment(ref _standIn);
            else
            {
                BeforeActualVerification?.Invoke(user);
                Interlocked.Increment(ref _actual);
            }
            return Inner.VerifyHashedPassword(user, hash, password);
        }
    }

    private sealed class NoticeQueue : IOutboundEmailQueue
    {
        public ConcurrentQueue<OutboundEmail> Notices { get; } = new();
        public void Enqueue(OutboundEmail email) => Notices.Enqueue(email);
    }

    private sealed class PauseBeforeAddressClaim : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _paused;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("INSERT INTO identity.\"SignInAddressFailures\"", StringComparison.Ordinal) &&
                Interlocked.CompareExchange(ref _paused, 1, 0) == 0)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }

    private sealed class ObserveContenderAdmission : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) Reached.TrySetResult();
            return ValueTask.FromResult(result);
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            // The original implementation has no coordination command. Observe its completed
            // address claim instead so this same bounded test retains a red baseline signal.
            if (command.CommandText.TrimStart().StartsWith("INSERT INTO identity.\"SignInAddressFailures\"", StringComparison.Ordinal))
                Reached.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private static ServiceProvider Provider(DbContextOptions<AppDbContext> options, IPasswordHasher<ApplicationUser>? hasher = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new AppDbContext(options));
        services.AddIdentityCore<ApplicationUser>(identity =>
        {
            identity.Password.RequireNonAlphanumeric = false;
            identity.Lockout.AllowedForNewUsers = false;
        }).AddEntityFrameworkStores<AppDbContext>();
        if (hasher is not null) services.AddSingleton(hasher);
        return services.BuildServiceProvider();
    }

    private static SignInProtection Protection(ServiceProvider provider, NoticeQueue notices, SignInThrottleOptions? throttleOptions = null)
    {
        var db = provider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock();
        var policy = throttleOptions ?? Options;
        return new SignInProtection(
            provider.GetRequiredService<UserManager<ApplicationUser>>(), db, new ConfigurationBuilder().Build(),
            new SignInThrottleStore(db, clock, policy, NullLogger<SignInThrottleStore>.Instance),
            new KnownDeviceStore(db, clock, policy), notices, NullLogger<SignInProtection>.Instance);
    }

    private static async Task<string> CreateCookieAsync(DbContextOptions<AppDbContext> options, NoticeQueue notices, CancellationToken ct, string email = Email)
    {
        await using (var setup = Provider(options))
        {
            var manager = setup.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = email, Email = email, DisplayName = "Controlled User",
                EmailConfirmed = true,
            };
            Assert.True((await manager.CreateAsync(user, "ControlledPassword1")).Succeeded);
            return Assert.IsType<ProtectedSignIn>(await Protection(setup, notices).AuthenticateAsync(
                new LoginRequest(email, "ControlledPassword1"), "198.51.100.45", null, ct)).Device!.Token;
        }

    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AuthenticateAsync_ConcurrentScopesPreserveTheirAllowanceAndFallback(bool contenderDropsCookie, bool contenderHasFreshAddress)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await postgres.CreateOptionsAsync(ct);
        var notices = new NoticeQueue();
        var cookie = await CreateCookieAsync(options, notices, ct);

        var gate = new PauseBeforeAddressClaim();
        var hasher = new ObservedHasher();
        await using var callerA = Provider(postgres.WithInterceptors(options, gate), hasher);
        var contender = new ObserveContenderAdmission();
        await using var callerB = Provider(postgres.WithInterceptors(options, contender), hasher);
        await using var inspection = new AppDbContext(options);
        var request = new LoginRequest(Email, "WrongPassword1");

        // A pauses between its two claims. In the repaired path B is observed before its
        // coordination lock; on the original path B is observed after its address claim instead.
        // Release A at that observable checkpoint, never require B to finish while A holds a lock.
        var attemptA = Protection(callerA, notices).AuthenticateAsync(request, SourceAddress, cookie, ct);
        Task<ProtectedSignIn?>? attemptB = null;
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            attemptB = Protection(callerB, notices).AuthenticateAsync(request, contenderHasFreshAddress ? "203.0.113.46" : SourceAddress, contenderDropsCookie ? null : cookie, ct);
            await contender.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        var resultA = await attemptA.WaitAsync(TimeSpan.FromSeconds(10), ct);
        var resultB = await attemptB!.WaitAsync(TimeSpan.FromSeconds(10), ct);
        output.WriteLine("Both completed; allowance={0}; checked passwords={1}; stand-in work={2}; notices={3}; both refusals={4}.",
            Options.PerAddressMaxFailures, hasher.Actual, hasher.StandIn, notices.Notices.Count, resultA is null && resultB is null);
        Assert.Null(resultA);
        Assert.Null(resultB);
        var expectedActualChecks = contenderHasFreshAddress ? 2 : Options.PerAddressMaxFailures;
        Assert.True(hasher.Actual == expectedActualChecks,
            $"Two callers checked {hasher.Actual} real passwords; expected {expectedActualChecks} for these scopes.");
        Assert.Equal(contenderHasFreshAddress ? 0 : 1, hasher.StandIn);
        Assert.Single(notices.Notices);
    }
    [Fact]
    public async Task AuthenticateAsync_SequentialControlChecksOnlyOneRealPassword()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await postgres.CreateOptionsAsync(ct);
        var notices = new NoticeQueue();
        var cookie = await CreateCookieAsync(options, notices, ct);
        var hasher = new ObservedHasher();
        await using var callerA = Provider(options, hasher);
        await using var callerB = Provider(options, hasher);
        var request = new LoginRequest(Email, "WrongPassword1");
        Assert.Null(await Protection(callerA, notices).AuthenticateAsync(request, SourceAddress, cookie, ct));
        Assert.Null(await Protection(callerB, notices).AuthenticateAsync(request, SourceAddress, cookie, ct));
        output.WriteLine("Sequential control: real work={0}; stand-in work={1}; notices={2}.", hasher.Actual, hasher.StandIn, notices.Notices.Count);
        Assert.Equal(1, hasher.Actual);
        Assert.Equal(1, hasher.StandIn);
        Assert.Single(notices.Notices);
    }
    private sealed class FailAddressClaim(Exception failure, Action? beforeThrow = null) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("INSERT INTO identity.\"SignInAddressFailures\"", StringComparison.Ordinal))
            {
                beforeThrow?.Invoke();
                throw failure;
            }
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticateAsync_FailedAdmissionRollsBackDeviceClaimAndReleasesCoordination(bool cancellation)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await postgres.CreateOptionsAsync(ct);
        var notices = new NoticeQueue();
        var cookie = await CreateCookieAsync(options, notices, ct);
        var hasher = new ObservedHasher();
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var failure = cancellation ? (Exception)new OperationCanceledException(canceled.Token) : new InvalidOperationException("controlled admission fault");
        await using (var failing = Provider(postgres.WithInterceptors(options, new FailAddressClaim(failure, cancellation ? canceled.Cancel : null)), hasher))
        {
            var attempt = () => Protection(failing, notices).AuthenticateAsync(new LoginRequest(Email, "WrongPassword1"), SourceAddress, cookie, canceled.Token);
            if (cancellation)
            {
                var exception = await Assert.ThrowsAsync<OperationCanceledException>(attempt);
                Assert.Equal(canceled.Token, exception.CancellationToken);
            }
            else await Assert.ThrowsAsync<InvalidOperationException>(attempt);
        }
        Assert.Equal(0, hasher.Actual + hasher.StandIn);
        Assert.Empty(notices.Notices);
        await using var inspection = new AppDbContext(options);
        Assert.Equal(0, await inspection.KnownDevices.Select(device => device.FailureCount).SingleAsync(ct));
        Assert.False(await inspection.SignInAddressFailures.AnyAsync(row => row.IpAddress == SourceAddress, ct));
        await using var recovery = Provider(options);
        Assert.NotNull(await Protection(recovery, notices).AuthenticateAsync(new LoginRequest(Email, "ControlledPassword1"), SourceAddress, cookie, ct)
            .WaitAsync(TimeSpan.FromSeconds(10), ct));
    }

    [Fact]
    public async Task AuthenticateAsync_UserCoordinationHasEndedBeforePasswordVerification()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await postgres.CreateOptionsAsync(ct);
        var notices = new NoticeQueue();
        var cookie = await CreateCookieAsync(options, notices, ct);
        var hasher = new ObservedHasher();
        await using var inspection = new AppDbContext(options);
        hasher.BeforeActualVerification = user =>
        {
            using var connection = new NpgsqlConnection(inspection.Database.GetConnectionString());
            connection.Open();
            using var command = new NpgsqlCommand("SELECT \"Id\" FROM identity.\"Users\" WHERE \"Id\" = @id FOR UPDATE NOWAIT", connection);
            command.Parameters.AddWithValue("id", user.Id);
            Assert.Equal(user.Id, command.ExecuteScalar());
        };
        await using var caller = Provider(options, hasher);
        Assert.Null(await Protection(caller, notices).AuthenticateAsync(new LoginRequest(Email, "WrongPassword1"), SourceAddress, cookie, ct));
        Assert.Equal(1, hasher.Actual);
    }

    [Fact]
    public async Task AuthenticateAsync_CoordinationForOneUserDoesNotBlockAnotherUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await postgres.CreateOptionsAsync(ct);
        var notices = new NoticeQueue();
        var cookieA = await CreateCookieAsync(options, notices, ct);
        const string otherEmail = "independent-user@example.com";
        var cookieB = await CreateCookieAsync(options, notices, ct, otherEmail);
        var gate = new PauseBeforeAddressClaim();
        var hasher = new ObservedHasher();
        await using var callerA = Provider(postgres.WithInterceptors(options, gate), hasher);
        await using var callerB = Provider(options, hasher);
        var attemptA = Protection(callerA, notices).AuthenticateAsync(new LoginRequest(Email, "WrongPassword1"), SourceAddress, cookieA, ct);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.Null(await Protection(callerB, notices).AuthenticateAsync(new LoginRequest(otherEmail, "WrongPassword1"), SourceAddress, cookieB, ct)
                .WaitAsync(TimeSpan.FromSeconds(10), ct));
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        Assert.Null(await attemptA.WaitAsync(TimeSpan.FromSeconds(10), ct));
        Assert.Equal(2, hasher.Actual);
        Assert.Equal(0, hasher.StandIn);
        Assert.Equal(2, notices.Notices.Count);
    }
    private sealed class RemoveUserBeforeCoordination(DbContextOptions<AppDbContext> options) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                await using var removal = new AppDbContext(options);
                await removal.Users.Where(user => user.Email == Email).ExecuteDeleteAsync(cancellationToken);
            }
            return result;
        }
    }

    [Fact]
    public async Task AuthenticateAsync_UserRemovedBeforeCoordinationReturnsGenericRefusal()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await postgres.CreateOptionsAsync(ct);
        var notices = new NoticeQueue();
        var cookie = await CreateCookieAsync(options, notices, ct);
        var hasher = new ObservedHasher();
        await using var caller = Provider(postgres.WithInterceptors(options, new RemoveUserBeforeCoordination(options)), hasher);
        Assert.Null(await Protection(caller, notices).AuthenticateAsync(new LoginRequest(Email, "WrongPassword1"), SourceAddress, cookie, ct));
        Assert.Equal(0, hasher.Actual);
        Assert.Equal(1, hasher.StandIn);
        Assert.Empty(notices.Notices);
    }

    [Fact]
    public async Task AuthenticateAsync_OriginalTwelveCallerBurstChecksOnlyTheSharedAllowance()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await postgres.CreateOptionsAsync(ct);
        var notices = new NoticeQueue();
        var cookie = await CreateCookieAsync(options, notices, ct);
        var hasher = new ObservedHasher();
        var policy = SignInThrottle.Defaults with { PerAddressMaxFailures = 3 };
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var caller = Provider(options, hasher);
            return await Protection(caller, notices, policy).AuthenticateAsync(new LoginRequest(Email, "WrongPassword1"), SourceAddress, cookie, ct);
        })).WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.All(results, result => Assert.Null(result));
        Assert.Equal(3, hasher.Actual);
        Assert.Equal(9, hasher.StandIn);
        Assert.Single(notices.Notices);
    }
}
