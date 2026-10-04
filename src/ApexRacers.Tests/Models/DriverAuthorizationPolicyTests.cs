using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Models;

public sealed class DriverAuthorizationPolicyTests
{
    [Theory]
    [InlineData(DriverLifecycleKind.WithdrawSharing, DriverConsentScope.Personal, false)]
    [InlineData(DriverLifecycleKind.WithdrawSharing, DriverConsentScope.Sharing, true)]
    [InlineData(DriverLifecycleKind.WithdrawPersonal, DriverConsentScope.Personal, true)]
    [InlineData(DriverLifecycleKind.WithdrawPersonal, DriverConsentScope.Sharing, true)]
    [InlineData(DriverLifecycleKind.RevokeProof, DriverConsentScope.Personal, true)]
    [InlineData(DriverLifecycleKind.Unlink, DriverConsentScope.Sharing, true)]
    [InlineData(DriverLifecycleKind.DeleteUser, DriverConsentScope.Personal, true)]
    public void TransitionScopeIsExplicit(DriverLifecycleKind kind, DriverConsentScope purpose, bool affected) =>
        Assert.Equal(affected, DriverAuthorizationPolicy.Affects(kind, purpose));

    [Fact]
    public void DurableClockNormalizesBeforeJournalPersistenceWithoutMovingItForward()
    {
        var original = new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.FromHours(-4)).AddTicks(1234567);
        var durable = DriverAuthorizationPolicy.DurableTime(original);
        Assert.Equal(TimeSpan.Zero, durable.Offset);
        Assert.Equal(original.UtcTicks - 7, durable.UtcTicks);
        Assert.Equal(durable, DriverAuthorizationPolicy.DurableTime(durable));
    }

    [Fact]
    public void PendingSharingIntentDoesNotVetoPersonalButCompletedRevisionVetoesRestore()
    {
        var scope = new DriverScope(Guid.NewGuid(), 1, DataProvenance.Demo);
        var intent = new DriverLifecycleIntent(Guid.NewGuid(), Guid.NewGuid(), scope,
            DriverLifecycleKind.WithdrawSharing, DateTimeOffset.UtcNow);
        var pending = new DriverJournalState(true, 1, [intent]);
        Assert.True(pending.Allows(1, DriverConsentScope.Personal));
        Assert.False(pending.Allows(1, DriverConsentScope.Sharing));
        Assert.False((pending with { Available = false }).Allows(1, DriverConsentScope.Personal));
        Assert.False((pending with { MinimumRevision = 2 }).Allows(1, DriverConsentScope.Personal));
        Assert.False((pending with { PendingIntents = [intent with { Kind = DriverLifecycleKind.Unlink }] })
            .Allows(3, DriverConsentScope.Personal));
    }

    [Fact]
    public async Task UnavailableDefaultAdaptersNeverSupplyProofOrAcknowledgment()
    {
        var ct = TestContext.Current.CancellationToken;
        var scope = new DriverScope(Guid.NewGuid(), 1, DataProvenance.Real);
        var journal = new UnavailableDriverEnforcementJournal();
        Assert.Null(await new UnavailableDriverOwnershipProof().VerifyAsync(scope, ct));
        Assert.False((await journal.ReadAsync(scope, ct)).Available);
        var intent = new DriverLifecycleIntent(Guid.NewGuid(), Guid.NewGuid(), scope, DriverLifecycleKind.Unlink, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.AppendAsync(intent, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.ReconcileAsync(intent, 2, ct));
    }
}
