using System.Collections.Immutable;
using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Operating;

public sealed class DriverOperatingPolicyTests
{
    private static readonly Guid User = Guid.Parse("aaaaaaaa-3790-4000-8000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PilotAdmitsOnlyExplicitUsersAndNeverUsesAdminAsAnExemption()
    {
        var state = Pilot();
        Assert.True(DriverOperatingPolicy.Allows(state, Request(User), Now));
        Assert.False(DriverOperatingPolicy.Allows(state, Request(Guid.NewGuid()) with { Audience = OperatingAudience.Admin }, Now));
        Assert.False(DriverOperatingPolicy.Allows(state, Request(null), Now));
    }

    internal static DriverOperatingState Pilot() => new(
        new("synthetic379", 1, DataProvenance.Demo, [new("reference", "official", OperatingWork.Publication)],
            [User], new(10, 10, TimeSpan.FromMinutes(1), "synthetic379-budget", DataProvenance.Demo, Verified: true)),
        DriverOperatingStage.Pilot, Now, Now, 1, [new(User, 1)],
        [new(DriverOperatingStage.Smoke, Now.AddDays(-1), "synthetic-smoke", true, true, true),
         new(DriverOperatingStage.Pilot, Now, "synthetic-pilot", true, true, true)], [], null);

    internal static OperatingRequest Request(Guid? user) => new("reference", "official", OperatingWork.Publication,
        DataProvenance.Demo, user, OperatingAudience.Standard);

    [Fact]
    public void MissingBudgetOversizedAllowlistUnknownScopeAndRealUseStayClosed()
    {
        var state = Pilot();
        Assert.False(DriverOperatingPolicy.Allows(state with { Manifest = state.Manifest with { Budget = null } }, Request(User), Now));
        Assert.False(DriverOperatingPolicy.Allows(state with { Manifest = state.Manifest with { Allowlist = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToImmutableArray() } }, Request(User), Now));
        Assert.False(DriverOperatingPolicy.Allows(state, Request(User) with { CollectionScope = "unreviewed" }, Now));
        Assert.False(DriverOperatingPolicy.Allows(state, Request(User) with { Provenance = DataProvenance.Real }, Now));
    }

    [Fact]
    public void PromotionRequiresApprovalAndObservationRatherThanElapsedTimeAlone()
    {
        var state = Pilot();
        var approval = new OperatingApproval(DriverOperatingStage.Alpha, Now.AddDays(14), "synthetic-alpha", true, true, true);
        Assert.Null(DriverOperatingPolicy.Promote(state, approval with { At = Now.AddDays(14).AddTicks(-1) }, Now.AddDays(14)));
        Assert.Null(DriverOperatingPolicy.Promote(state, approval with { CompositionReviewed = false }, Now.AddDays(14)));
        var alpha = Assert.IsType<DriverOperatingState>(DriverOperatingPolicy.Promote(state, approval, Now.AddDays(14)));
        Assert.Equal(DriverOperatingStage.Alpha, alpha.Stage);
        Assert.Equal(Now, alpha.PilotStartedAt);
        Assert.False(DriverOperatingPolicy.Allows(alpha, Request(User), Now.AddDays(14)));
        Assert.True(DriverOperatingPolicy.Allows(alpha, Request(User) with { Audience = OperatingAudience.Alpha }, Now.AddDays(14)));
    }

    [Fact]
    public void RecoveryNeedsCauseSpecificEvidenceAndRestartsMaterialPilotObservation()
    {
        var state = Pilot();
        var stop = new OperatingStop(Guid.NewGuid(), "lost-accounting", Now.AddDays(20), true);
        var stopped = DriverOperatingPolicy.Stop(state, stop, stop.At)!;
        Assert.False(DriverOperatingPolicy.Allows(stopped, Request(User), stop.At));
        var recovery = new OperatingRecovery(stop.Id, stop.Cause, "synthetic-repair", true, true, true, true,
            new(DriverOperatingStage.Pilot, stop.At.AddDays(1), "synthetic-reapproval", true, true, true));
        Assert.Null(DriverOperatingPolicy.Recover(stopped, recovery with { Cause = "other" }, recovery.Approval.At));
        Assert.Null(DriverOperatingPolicy.Recover(stopped, recovery with { PhysicalCleanupVerified = false }, recovery.Approval.At));
        var resumed = DriverOperatingPolicy.Recover(stopped, recovery, recovery.Approval.At)!;
        Assert.Equal(recovery.Approval.At, resumed.PilotStartedAt);
        Assert.Equal(state.Stops.Length + 1, resumed.Stops.Length);
        Assert.True(DriverOperatingPolicy.Allows(resumed, Request(User), recovery.Approval.At));
        Assert.Null(DriverOperatingPolicy.Promote(resumed,
            recovery.Approval with { Stage = DriverOperatingStage.Alpha, At = stop.At.AddDays(14) }, stop.At.AddDays(14)));
    }

    [Fact]
    public void MaterialScopeReplacementRequiresFreshOptInAndFreshSmokeApproval()
    {
        var state = Pilot();
        var manifest = state.Manifest with { ScopeVersion = 2 };
        var approval = new OperatingApproval(DriverOperatingStage.Smoke, Now, "synthetic-scope-review", true, true, true, ScopeVersion: 2);
        Assert.Null(DriverOperatingPolicy.ChangeScope(state, manifest, approval with { ScopeVersion = 1 }, Now));
        var changed = DriverOperatingPolicy.ChangeScope(state, manifest, approval, Now)!;
        Assert.Empty(changed.OptIns);
        Assert.False(DriverOperatingPolicy.Allows(changed, Request(User), Now));
        Assert.Equal(state.Approvals.Length + 1, changed.Approvals.Length);
    }

    [Fact]
    public void CatalogRevisionIsPartOfOperatingScopeRatherThanAnInterchangeableLabel()
    {
        var state = Pilot();
        Assert.False(DriverOperatingPolicy.Allows(state, Request(User) with { CatalogRevision = 2 }, Now));
    }

    [Fact]
    public void ADeclaredNumberWithoutVerifiedBudgetEvidenceCannotOpenProcessing()
    {
        var state = Pilot();
        Assert.False(DriverOperatingPolicy.Allows(state with { Manifest = state.Manifest with
            { Budget = state.Manifest.Budget! with { Verified = false } } }, Request(User), Now));
    }

    [Fact]
    public void RecoveryOfOneFailureCannotClearAnotherUnresolvedSafetyCause()
    {
        var first = new OperatingStop(Guid.NewGuid(), "lost-accounting", Now, true);
        var second = new OperatingStop(Guid.NewGuid(), "missed-cleanup", Now, false);
        var stopped = DriverOperatingPolicy.Stop(Pilot(), first, Now)!;
        stopped = Assert.IsType<DriverOperatingState>(DriverOperatingPolicy.Stop(stopped, second, Now));
        var approval = new OperatingApproval(DriverOperatingStage.Pilot, Now.AddDays(1), "synthetic-approval", true, true, true);
        var one = DriverOperatingPolicy.Recover(stopped, new(first.Id, first.Cause, "synthetic-cause-one", true, true, true, true, approval), approval.At)!;
        Assert.False(DriverOperatingPolicy.Allows(one, Request(User), approval.At));
        var finalApproval = approval with { At = Now.AddDays(2) };
        var both = DriverOperatingPolicy.Recover(one, new(second.Id, second.Cause, "synthetic-cause-two", true, true, true, true, finalApproval), finalApproval.At)!;
        Assert.True(DriverOperatingPolicy.Allows(both, Request(User), finalApproval.At));
        Assert.Equal(finalApproval.At, both.PilotStartedAt);
        Assert.Equal(2, both.Recoveries.Length);
    }

    [Theory]
    [InlineData(DriverOperatingStage.Alpha, 7)]
    [InlineData(DriverOperatingStage.Beta, 7)]
    public void AlphaAndBetaNeedSevenDaysAndStandardNeedsVisitorEvidence(DriverOperatingStage stage, int days)
    {
        var initial = Pilot();
        var state = initial with { Stage = stage, Approvals = initial.Approvals.Add(new(stage, Now, "synthetic-stage", true, true, true)) };
        var next = new OperatingApproval((DriverOperatingStage)((int)stage + 1), Now.AddDays(days), "synthetic-next", true, true, true, true);
        Assert.Null(DriverOperatingPolicy.Promote(state, next with { At = next.At.AddTicks(-1) }, next.At));
        Assert.NotNull(DriverOperatingPolicy.Promote(state, next, next.At));
        if (stage == DriverOperatingStage.Beta)
        {
            Assert.Null(DriverOperatingPolicy.Promote(state, next with { VisitorOutputVerified = false }, next.At));
            var standard = DriverOperatingPolicy.Promote(state, next, next.At)!;
            Assert.True(DriverOperatingPolicy.Allows(standard, Request(null) with { Audience = OperatingAudience.Visitor }, next.At));
        }
    }

    [Fact]
    public void NonmaterialRecoveryKeepsOriginalObservationAndRealBudgetCannotAuthorizeDemo()
    {
        var state = Pilot();
        var stop = new OperatingStop(Guid.NewGuid(), "invalidated-prerequisite", Now.AddDays(2), false);
        var stopped = DriverOperatingPolicy.Stop(state, stop, stop.At)!;
        var approval = new OperatingApproval(DriverOperatingStage.Pilot, Now.AddDays(3), "synthetic-recovered", true, true, true);
        var recovered = DriverOperatingPolicy.Recover(stopped, new(stop.Id, stop.Cause, "synthetic-reproduction", true, true, true, true, approval), approval.At)!;
        Assert.Equal(Now, recovered.PilotStartedAt);
        Assert.False(DriverOperatingPolicy.Allows(state with { Manifest = state.Manifest with { Budget = state.Manifest.Budget! with { Provenance = DataProvenance.Real } } }, Request(User), Now));
    }
}
