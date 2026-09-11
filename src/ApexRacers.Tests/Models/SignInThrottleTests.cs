using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Models;

/// <summary>
/// The decision rules behind issue #300's fix, exercised without a database.
/// </summary>
public class SignInThrottleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly SignInThrottleOptions Options = new(
        PerAddressMaxFailures: 5,
        TightenedPerAddressMaxFailures: 1,
        AccountHighWaterFailures: 50,
        PerAddressWindow: TimeSpan.FromMinutes(15),
        AccountWindow: TimeSpan.FromHours(1),
        NoticeInterval: TimeSpan.FromHours(1));

    [Fact]
    public void Evaluate_NoHistory_Allows()
    {
        var decision = SignInThrottle.Evaluate(null, null, Now, Options);

        Assert.False(decision.Refused);
        Assert.False(decision.UnderAttack);
        Assert.Equal(5, decision.Allowance);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(9, true)]
    public void Evaluate_RefusesOnceTheAddressReachesItsAllowance(int failures, bool expectRefused)
    {
        var decision = SignInThrottle.Evaluate(
            new FailureWindow(failures, Now), null, Now, Options);

        Assert.Equal(expectRefused, decision.Refused);
    }

    [Fact]
    public void Evaluate_AddressWindowExpired_AllowsAgain()
    {
        var spent = new FailureWindow(99, Now);

        Assert.True(SignInThrottle.Evaluate(spent, null, Now + TimeSpan.FromMinutes(14), Options).Refused);
        Assert.False(SignInThrottle.Evaluate(spent, null, Now + TimeSpan.FromMinutes(15), Options).Refused);
    }

    /// <summary>
    /// The account-wide counter shrinks the per-address allowance. It must never refuse an address
    /// that has not itself failed — that would rebuild the denial of service #300 removed.
    /// </summary>
    [Fact]
    public void Evaluate_AccountOverHighWater_TightensButStillAdmitsACleanAddress()
    {
        var attacked = new FailureWindow(50, Now);

        var clean = SignInThrottle.Evaluate(null, attacked, Now, Options);
        Assert.True(clean.UnderAttack);
        Assert.Equal(1, clean.Allowance);
        Assert.False(clean.Refused);

        var hasFailedOnce = SignInThrottle.Evaluate(new FailureWindow(1, Now), attacked, Now, Options);
        Assert.True(hasFailedOnce.Refused);
    }

    [Fact]
    public void Evaluate_AccountWindowExpired_ReturnsToTheFullAllowance()
    {
        var stale = new FailureWindow(500, Now);
        var later = Now + TimeSpan.FromHours(1);

        var decision = SignInThrottle.Evaluate(new FailureWindow(1, later), stale, later, Options);

        Assert.False(decision.UnderAttack);
        Assert.Equal(5, decision.Allowance);
        Assert.False(decision.Refused);
    }

    [Fact]
    public void Evaluate_JustUnderTheHighWater_IsNotTightened()
    {
        var decision = SignInThrottle.Evaluate(null, new FailureWindow(49, Now), Now, Options);

        Assert.False(decision.UnderAttack);
        Assert.Equal(5, decision.Allowance);
    }

    /// <summary>
    /// The property that makes a window un-extendable: a failure inside a live window counts up but
    /// never moves the start, so the window always ends a fixed span after the failure that opened it.
    /// </summary>
    [Fact]
    public void RecordFailure_WithinTheWindow_CountsUpWithoutMovingTheStart()
    {
        var opened = new FailureWindow(1, Now);

        var next = SignInThrottle.RecordFailure(opened, Now + TimeSpan.FromMinutes(14), Options.PerAddressWindow);

        Assert.Equal(2, next.Count);
        Assert.Equal(Now, next.StartedAt);
    }

    [Fact]
    public void RecordFailure_AfterTheWindow_StartsAFreshOne()
    {
        var opened = new FailureWindow(5, Now);
        var later = Now + TimeSpan.FromMinutes(15);

        var next = SignInThrottle.RecordFailure(opened, later, Options.PerAddressWindow);

        Assert.Equal(1, next.Count);
        Assert.Equal(later, next.StartedAt);
    }

    [Fact]
    public void RecordFailure_NoExistingWindow_StartsAtOne()
    {
        var next = SignInThrottle.RecordFailure(null, Now, Options.PerAddressWindow);

        Assert.Equal(1, next.Count);
        Assert.Equal(Now, next.StartedAt);
    }

    /// <summary>
    /// Repeated failures across a long run can never push the window's end out, however many arrive.
    /// Walks the exact shape an attacker would use against the old account-wide lockout.
    /// </summary>
    [Fact]
    public void RecordFailure_SustainedFailures_CannotHoldAWindowOpen()
    {
        var window = SignInThrottle.RecordFailure(null, Now, Options.PerAddressWindow);

        for (var minute = 1; minute <= 14; minute++)
            window = SignInThrottle.RecordFailure(window, Now + TimeSpan.FromMinutes(minute), Options.PerAddressWindow);

        Assert.Equal(Now, window.StartedAt);
        Assert.False(SignInThrottle.IsCurrent(window, Now + Options.PerAddressWindow, Options.PerAddressWindow));
    }

    [Fact]
    public void LiveCount_ExpiredWindow_ReadsAsZero()
    {
        var window = new FailureWindow(7, Now);

        Assert.Equal(7, SignInThrottle.LiveCount(window, Now, Options.PerAddressWindow));
        Assert.Equal(0, SignInThrottle.LiveCount(window, Now + TimeSpan.FromMinutes(15), Options.PerAddressWindow));
        Assert.Equal(0, SignInThrottle.LiveCount(null, Now, Options.PerAddressWindow));
    }

    [Fact]
    public void IsCurrent_AtTheExactBoundary_HasExpired()
    {
        var window = new FailureWindow(1, Now);

        Assert.True(SignInThrottle.IsCurrent(window, Now + TimeSpan.FromMinutes(14.999), Options.PerAddressWindow));
        Assert.False(SignInThrottle.IsCurrent(window, Now + Options.PerAddressWindow, Options.PerAddressWindow));
    }

    [Fact]
    public void Defaults_MatchTheDocumentedPolicy()
    {
        Assert.Equal(5, SignInThrottle.Defaults.PerAddressMaxFailures);
        // One, never zero: zero would deny the owner's own address too.
        Assert.Equal(1, SignInThrottle.Defaults.TightenedPerAddressMaxFailures);
        Assert.Equal(50, SignInThrottle.Defaults.AccountHighWaterFailures);
        Assert.Equal(TimeSpan.FromMinutes(15), SignInThrottle.Defaults.PerAddressWindow);
        Assert.Equal(TimeSpan.FromHours(1), SignInThrottle.Defaults.AccountWindow);
        Assert.Equal(TimeSpan.FromHours(1), SignInThrottle.Defaults.NoticeInterval);
    }

    /// <summary>
    /// The tightened allowance must stay above zero. A zero here would silently turn the
    /// under-attack state back into the account-wide denial of service.
    /// </summary>
    [Fact]
    public void TightenedAllowance_IsNeverZero()
    {
        Assert.True(SignInThrottle.TightenedPerAddressMaxFailures >= 1);

        var decision = SignInThrottle.Evaluate(
            null, new FailureWindow(int.MaxValue, Now), Now, SignInThrottle.Defaults);

        Assert.False(decision.Refused);
    }
}
