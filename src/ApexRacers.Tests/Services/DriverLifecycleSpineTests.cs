using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public sealed class DriverLifecycleSpineTests(PostgreSqlFixture postgres)
{
    private static readonly DriverConsent Personal = new(DriverAuthorizationPolicy.PersonalConsentVersion);
    private static readonly DriverConsent Both = Personal with { SharingVersion = DriverAuthorizationPolicy.SharingConsentVersion };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MatchingProofAndSeparateOptInsGrantOnlySelectedScopes()
    {
        await using var fixture = await CreateAsync();
        Assert.Null(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Personal, Ct));
        var first = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        Assert.NotNull(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Personal, Ct));
        Assert.Null(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Sharing, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(fixture.Scope, Both, Ct));
        fixture.Proof.Receipt = fixture.Proof.Receipt! with { ReceiptId = Guid.NewGuid() };
        var second = await fixture.Authority.GrantAsync(fixture.Scope, Both, Ct);
        Assert.True(second.Revision > first.Revision);
        Assert.NotNull(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Sharing, Ct));
        Assert.True(await fixture.Store.HasAssociationAsync(fixture.Scope.UserId, Ct));
        Assert.False(await fixture.Store.HasAssociationAsync(Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task ScopeContractionMustUseJournalFirstLifecycleNotGrantMutation()
    {
        await using var fixture = await CreateAsync();
        await fixture.Authority.GrantAsync(fixture.Scope, Both, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct));
        Assert.NotNull(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Sharing, Ct));
    }

    [Fact]
    public async Task ForgedReceiptAndEvenSyntheticRealAdapterCannotGrantReal()
    {
        await using var fixture = await CreateAsync();
        fixture.Proof.Receipt = fixture.Proof.Receipt! with { Scope = fixture.Scope with { UserId = Guid.NewGuid() } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct));
        var real = fixture.Scope with { Provenance = DataProvenance.Real };
        fixture.Proof.Receipt = fixture.Proof.Receipt with { Scope = real };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(real, Both, Ct));
        Assert.Null(await fixture.Authority.ResolveAsync(real, DriverConsentScope.Personal, Ct));
        Assert.Null(await fixture.Authority.ResolveAsync(real with { Provenance = DataProvenance.Unknown }, DriverConsentScope.Personal, Ct));
        Assert.Empty(await fixture.Db.Set<DriverAuthorizationGrant>().ToListAsync(Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("old-personal-version")]
    public async Task MissingOrObsoleteConsentIsNotOwnership(string version)
    {
        await using var fixture = await CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Authority.GrantAsync(fixture.Scope, new(version), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Authority.GrantAsync(fixture.Scope, Personal with { SharingVersion = "old" }, Ct));
        Assert.Empty(await fixture.Db.Set<DriverProofReceipt>().ToListAsync(Ct));
    }

    [Fact]
    public async Task UnavailableProofOrJournalCannotGrantAndCannotReadWarmGrant()
    {
        await using var fixture = await CreateAsync();
        fixture.Proof.Receipt = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct));
        fixture.Proof.Receipt = fixture.OriginalProof;
        await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        fixture.Journal.Available = false;
        Assert.Null(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Personal, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.Unlink, Guid.NewGuid(), Ct));
        Assert.Empty(await fixture.Db.Set<DriverLifecycleOperation>().ToListAsync(Ct));
        fixture.Journal.Available = true;
        Assert.NotNull(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Personal, Ct));
    }

    [Fact]
    public async Task SharingWithdrawalKeepsPersonalAndInvalidatesOnlySharingCopies()
    {
        await using var fixture = await CreateAsync();
        var personal = await fixture.Authority.GrantAsync(fixture.Scope, Both, Ct);
        var sharing = (await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Sharing, Ct))!;
        var copies = new CopyLifecycle(fixture.Authority, fixture.Store, fixture.Journal);
        var personalCopy = await copies.CommitAsync(personal, "synthetic-private-payload", Ct);
        var nameCopy = await copies.CommitAsync(sharing, "Synthetic Driver", Ct);
        var outcome = await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawSharing, Guid.NewGuid(), Ct);
        Assert.True(outcome.Completed);
        Assert.NotNull(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Personal, Ct));
        Assert.Null(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Sharing, Ct));
        Assert.Null((await fixture.Db.Set<DriverTrackedCopy>().AsNoTracking().SingleAsync(c => c.Id == personalCopy, Ct)).UnavailableAt);
        Assert.NotNull((await fixture.Db.Set<DriverTrackedCopy>().AsNoTracking().SingleAsync(c => c.Id == nameCopy, Ct)).UnavailableAt);
        var cleanup = await fixture.Db.Set<DriverCopyCleanup>().SingleAsync(Ct);
        Assert.Equal(outcome.OriginalLossAt.AddHours(24), cleanup.DueAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => copies.CommitAsync(sharing, "stale-name", Ct));
    }

    [Theory]
    [InlineData(DriverLifecycleKind.WithdrawPersonal)]
    [InlineData(DriverLifecycleKind.Unlink)]
    [InlineData(DriverLifecycleKind.RevokeProof)]
    [InlineData(DriverLifecycleKind.DeleteUser)]
    public async Task PersonalLossClosesBothScopesWithDurableOriginalClock(DriverLifecycleKind kind)
    {
        await using var fixture = await CreateAsync();
        await fixture.Authority.GrantAsync(fixture.Scope, Both, Ct);
        var operationId = Guid.NewGuid();
        var first = await fixture.Authority.TransitionAsync(fixture.Scope, kind, operationId, Ct);
        fixture.Time.Now += TimeSpan.FromDays(2);
        var retry = await fixture.Authority.TransitionAsync(fixture.Scope, kind, operationId, Ct);
        Assert.True(first.Completed && retry.Completed);
        Assert.Equal(first.OriginalLossAt, retry.OriginalLossAt);
        Assert.Null(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Personal, Ct));
        Assert.Null(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Sharing, Ct));
        Assert.Single(await fixture.Db.Set<DriverLifecycleOperation>().ToListAsync(Ct));
        var work = await fixture.Db.Set<DriverCopyCleanup>().OrderBy(w => w.Purpose).ToListAsync(Ct);
        Assert.Equal(2, work.Count);
        Assert.All(work, item => Assert.Equal(first.OriginalLossAt, item.OriginalLossAt));
        Assert.Equal(first.OriginalLossAt.AddDays(kind == DriverLifecycleKind.DeleteUser ? 7 : 97), work[0].DueAt);
        Assert.Equal(first.OriginalLossAt.AddHours(24), work[1].DueAt);
    }

    [Fact]
    public async Task JournalIntentVetoesEvenWhenPrimaryCommitNeverHappened()
    {
        await using var fixture = await CreateAsync();
        var access = await fixture.Authority.GrantAsync(fixture.Scope, Both, Ct);
        var operationId = Guid.NewGuid();
        var observer = new FaultObserver("journal-intent-recorded");
        var interrupted = new DriverAuthorization(fixture.Store, fixture.Journal, fixture.Proof, fixture.Time, observer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => interrupted.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawPersonal, operationId, Ct));
        Assert.Empty(await fixture.Db.Set<DriverLifecycleOperation>().ToListAsync(Ct));
        Assert.Null(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Personal, Ct));
        Assert.Null(await fixture.Store.AdmitAsync(access, Guid.NewGuid(), fixture.Journal, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CommitCopyAsync(access, "stale", fixture.Journal, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(fixture.Scope, Both, Ct));
        var canonical = fixture.Journal.Intents[operationId];
        fixture.Time.Now += TimeSpan.FromDays(1);
        var recovered = await fixture.Authority.RecoverAsync(canonical, Ct);
        Assert.True(recovered.Completed);
        Assert.Equal(canonical.OriginalLossAt, recovered.OriginalLossAt);
    }

    [Theory]
    [InlineData("before-journal", 0, 0)]
    [InlineData("journal-intent-recorded", 1, 0)]
    [InlineData("primary-closed", 1, 1)]
    [InlineData("drained", 1, 1)]
    [InlineData("journal-completed", 1, 1)]
    public async Task EveryDurableBoundaryReplaysWithoutDuplicateCopyWork(string boundary, int journals, int primary)
    {
        await using var fixture = await CreateAsync();
        await fixture.Authority.GrantAsync(fixture.Scope, Both, Ct);
        var operationId = Guid.NewGuid();
        var interrupted = new DriverAuthorization(fixture.Store, fixture.Journal, fixture.Proof, fixture.Time, new FaultObserver(boundary));
        await Assert.ThrowsAsync<InvalidOperationException>(() => interrupted.TransitionAsync(fixture.Scope, DriverLifecycleKind.Unlink, operationId, Ct));
        Assert.Equal(journals, fixture.Journal.Intents.Count);
        Assert.Equal(primary, await fixture.Db.Set<DriverLifecycleOperation>().CountAsync(Ct));
        var recovered = await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.Unlink, operationId, Ct);
        Assert.True(recovered.Completed);
        Assert.Single(fixture.Journal.Intents);
        Assert.Equal(2, await fixture.Db.Set<DriverCopyCleanup>().CountAsync(Ct));
    }

    [Fact]
    public async Task ExpiredLeaseAndReplacementIncarnationNeverProveTerminality()
    {
        await using var fixture = await CreateAsync();
        var access = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        var incarnation = Guid.NewGuid();
        var admission = (await fixture.Store.AdmitAsync(access, incarnation, fixture.Journal, Ct))!;
        fixture.Time.Now += TimeSpan.FromHours(2);
        var operation = Guid.NewGuid();
        var pending = await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawPersonal, operation, Ct);
        Assert.False(pending.Completed);
        Assert.Equal(1, pending.PendingWriters);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CheckpointAsync(admission.Id, Guid.NewGuid(), Ct));
        Assert.False((await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawPersonal, operation, Ct)).Completed);
        await fixture.Store.CheckpointAsync(admission.Id, incarnation, Ct);
        Assert.True((await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawPersonal, operation, Ct)).Completed);
    }

    [Fact]
    public async Task PrimarySnapshotBehindIndependentJournalCannotRestorePermission()
    {
        await using var fixture = await CreateAsync();
        var access = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawPersonal, Guid.NewGuid(), Ct);
        // Deliberate ordinary-data rollback simulation; independently completed journal survives.
        await fixture.Db.Set<DriverAuthorizationGrant>().Where(g => g.Id == access.GrantId).ExecuteUpdateAsync(u =>
            u.SetProperty(g => g.Revision, access.Revision).SetProperty(g => g.PersonalConsentVersion, Personal.PersonalVersion), Ct);
        Assert.Null(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Personal, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct));
    }

    [Fact]
    public async Task FreshProofCannotTransferHistoricalOwnerOrReactivateDeletedGrant()
    {
        await using var fixture = await CreateAsync();
        await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.DeleteUser, Guid.NewGuid(), Ct);
        fixture.Time.Now += TimeSpan.FromHours(1);
        fixture.Proof.Receipt = fixture.OriginalProof with { ReceiptId = Guid.NewGuid(), VerifiedAt = fixture.Time.Now };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct));
        var stranger = new DriverScope(Guid.NewGuid(), fixture.Scope.CustomerId, DataProvenance.Demo);
        fixture.Db.Users.Add(new ApplicationUser { Id = stranger.UserId, UserName = "other", DisplayName = "Synthetic other", EmailConfirmed = true });
        await fixture.Db.SaveChangesAsync(Ct);
        fixture.Proof.Receipt = fixture.Proof.Receipt with { Scope = stranger };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(stranger, Personal, Ct));
        Assert.Single(await fixture.Db.Set<DriverAuthorizationGrant>().ToListAsync(Ct));
    }

    [Fact]
    public async Task DueCleanupActuallyRemovesCopiesWithoutErasingFreshGeneration()
    {
        await using var fixture = await CreateAsync();
        var old = await fixture.Authority.GrantAsync(fixture.Scope, Both, Ct);
        var copies = new CopyLifecycle(fixture.Authority, fixture.Store, fixture.Journal);
        await copies.CommitAsync(old, "old-private", Ct);
        await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawPersonal, Guid.NewGuid(), Ct);
        fixture.Time.Now += TimeSpan.FromDays(1);
        fixture.Proof.Receipt = fixture.OriginalProof with { ReceiptId = Guid.NewGuid(), VerifiedAt = fixture.Time.Now };
        var fresh = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        var freshId = await copies.CommitAsync(fresh, "fresh-private", Ct);
        fixture.Time.Now += TimeSpan.FromDays(98);
        Assert.Equal(2, await copies.ExecuteDueAsync(Ct));
        var retained = Assert.Single(await fixture.Db.Set<DriverTrackedCopy>().AsNoTracking().ToListAsync(Ct));
        Assert.Equal(freshId, retained.Id);
        Assert.All(await fixture.Db.Set<DriverCopyCleanup>().ToListAsync(Ct), w => Assert.NotNull(w.VerifiedRemovedAt));
        Assert.Equal(0, await copies.ExecuteDueAsync(Ct));
    }

    [Fact]
    public async Task StaleGenerationAndUnboundedCopiesAreRejected()
    {
        await using var fixture = await CreateAsync();
        var old = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        fixture.Proof.Receipt = fixture.OriginalProof with { ReceiptId = Guid.NewGuid() };
        var current = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        Assert.Null(await fixture.Store.AdmitAsync(old, Guid.NewGuid(), fixture.Journal, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CommitCopyAsync(old, "stale", fixture.Journal, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.CommitCopyAsync(current, new string('é', 8193), fixture.Journal, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.AdmitAsync(current, Guid.Empty, fixture.Journal, Ct));
        var admitted = (await fixture.Store.AdmitAsync(current, Guid.NewGuid(), fixture.Journal, Ct))!;
        fixture.Proof.Receipt = fixture.OriginalProof with { ReceiptId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct));
        await fixture.Store.CheckpointAsync(admitted.Id, admitted.Incarnation, Ct);
    }

    [Fact]
    public async Task OldCleanupCannotEraseALaterGenerationDuringItsOwnDormantWindow()
    {
        await using var fixture = await CreateAsync();
        var copies = new CopyLifecycle(fixture.Authority, fixture.Store, fixture.Journal);
        var old = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        await copies.CommitAsync(old, "old-generation", Ct);
        var firstLoss = await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawPersonal, Guid.NewGuid(), Ct);
        fixture.Time.Now += TimeSpan.FromDays(80);
        fixture.Proof.Receipt = fixture.OriginalProof with { ReceiptId = Guid.NewGuid(), VerifiedAt = fixture.Time.Now };
        var fresh = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        var freshCopy = await copies.CommitAsync(fresh, "new-generation", Ct);
        fixture.Time.Now += TimeSpan.FromDays(10);
        var nextLoss = await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawPersonal, Guid.NewGuid(), Ct);
        fixture.Time.Now = firstLoss.OriginalLossAt.AddDays(97);
        await copies.ExecuteDueAsync(Ct);
        Assert.Equal(freshCopy, Assert.Single(await fixture.Db.Set<DriverTrackedCopy>().AsNoTracking().ToListAsync(Ct)).Id);
        var pending = Assert.Single(await fixture.Db.Set<DriverCopyCleanup>().Where(w => w.VerifiedRemovedAt == null).ToListAsync(Ct));
        Assert.Equal(nextLoss.OriginalLossAt.AddDays(97), pending.DueAt);
        fixture.Time.Now = nextLoss.OriginalLossAt.AddDays(97);
        await copies.ExecuteDueAsync(Ct);
        Assert.Empty(await fixture.Db.Set<DriverTrackedCopy>().AsNoTracking().ToListAsync(Ct));
    }

    [Fact]
    public async Task OldOperationReplayDoesNotDrainFreshlyAuthorizedWriters()
    {
        await using var fixture = await CreateAsync();
        await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        var operationId = Guid.NewGuid();
        await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawPersonal, operationId, Ct);
        fixture.Time.Now += TimeSpan.FromHours(1);
        fixture.Proof.Receipt = fixture.OriginalProof with { ReceiptId = Guid.NewGuid(), VerifiedAt = fixture.Time.Now };
        var fresh = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        var writer = (await fixture.Store.AdmitAsync(fresh, Guid.NewGuid(), fixture.Journal, Ct))!;
        var replay = await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawPersonal, operationId, Ct);
        Assert.True(replay.Completed);
        Assert.NotNull(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Personal, Ct));
        await fixture.Store.CheckpointAsync(writer.Id, writer.Incarnation, Ct);
    }

    [Fact]
    public async Task RecoveryCannotAcknowledgeKnownLaggingPrimaryEvenWithExistingOperation()
    {
        await using var fixture = await CreateAsync();
        var first = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        var operationId = Guid.NewGuid();
        await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawPersonal, operationId, Ct);
        await fixture.Db.Set<DriverAuthorizationGrant>().Where(g => g.Id == first.GrantId).ExecuteUpdateAsync(u =>
            u.SetProperty(g => g.Revision, first.Revision).SetProperty(g => g.PersonalConsentVersion, Personal.PersonalVersion), Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.RecoverAsync(fixture.Journal.Intents[operationId], Ct));
        Assert.Null(await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Personal, Ct));
    }

    [Theory]
    [InlineData(89, true)]
    [InlineData(90, false)]
    [InlineData(97, false)]
    public async Task ReproofCannotExtendDormantReactivationBoundary(int days, bool allowed)
    {
        await using var fixture = await CreateAsync();
        await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        await fixture.Authority.TransitionAsync(fixture.Scope, DriverLifecycleKind.WithdrawPersonal, Guid.NewGuid(), Ct);
        fixture.Time.Now += TimeSpan.FromDays(days);
        fixture.Proof.Receipt = fixture.OriginalProof with { ReceiptId = Guid.NewGuid(), VerifiedAt = fixture.Time.Now };
        if (allowed) Assert.NotNull(await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct));
    }

    [Fact]
    public async Task NameChangesInvalidateUnsentResultsAndRequireActiveWriterDrain()
    {
        await using var fixture = await CreateAsync();
        var old = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        fixture.Proof.Receipt = fixture.OriginalProof with { DriverName = "Updated Synthetic Driver" };
        var changed = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        Assert.True(changed.Revision > old.Revision);
        Assert.Null(await fixture.Store.AdmitAsync(old, Guid.NewGuid(), fixture.Journal, Ct));
        var admission = (await fixture.Store.AdmitAsync(changed, Guid.NewGuid(), fixture.Journal, Ct))!;
        fixture.Proof.Receipt = fixture.Proof.Receipt with { DriverName = "Another Synthetic Name" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct));
        Assert.Equal(changed.Revision, (await fixture.Authority.ResolveAsync(fixture.Scope, DriverConsentScope.Personal, Ct))!.Revision);
        await fixture.Store.CheckpointAsync(admission.Id, admission.Incarnation, Ct);
    }

    [Fact]
    public async Task VerifiedSyntheticProofSupersedesAnAssertedClaimWithoutTransferringData()
    {
        await using var fixture = await CreateAsync();
        var priorUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = "asserted", DisplayName = "Synthetic asserted User",
            IRacingCustomerId = fixture.Scope.CustomerId, EmailConfirmed = true };
        fixture.Db.Users.Add(priorUser);
        await fixture.Db.SaveChangesAsync(Ct);
        var grant = await fixture.Authority.GrantAsync(fixture.Scope, Personal, Ct);
        Assert.Equal(fixture.Scope.UserId, grant.Scope.UserId);
        Assert.Equal(fixture.Scope.CustomerId, (await fixture.Db.Users.AsNoTracking().SingleAsync(u => u.Id == priorUser.Id, Ct)).IRacingCustomerId);
        Assert.False(await fixture.Store.HasAssociationAsync(priorUser.Id, Ct));
    }

    private async Task<Fixture> CreateAsync()
    {
        var db = await postgres.CreateDbContextAsync(Ct);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "synthetic", DisplayName = "Synthetic user", EmailConfirmed = true };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        return new Fixture(db, new DriverScope(user.Id, 123456, DataProvenance.Demo));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(AppDbContext db, DriverScope scope)
        {
            Db = db; Scope = scope;
            OriginalProof = new(Guid.NewGuid(), scope, Time.Now.AddDays(-1), "synthetic-test-only", "Synthetic Driver");
            Proof.Receipt = OriginalProof;
            Store = new(db, Time);
            Authority = new(Store, Journal, Proof, Time);
        }
        public AppDbContext Db { get; }
        public DriverScope Scope { get; }
        public MutableTime Time { get; } = new();
        public SyntheticProof Proof { get; } = new();
        public TestJournal Journal { get; } = new();
        public VerifiedDriverProof OriginalProof { get; }
        public DriverAuthorityStore Store { get; }
        public DriverAuthorization Authority { get; }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class MutableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class SyntheticProof : IDriverOwnershipProof
    {
        public VerifiedDriverProof? Receipt { get; set; }
        public Task<VerifiedDriverProof?> VerifyAsync(DriverScope scope, CancellationToken ct = default) => Task.FromResult(Receipt);
    }
    private sealed class FaultObserver(string boundary) : IDriverLifecycleObserver
    {
        public Task PhaseAsync(string phase, Guid operationId, CancellationToken ct = default) => phase == boundary
            ? Task.FromException(new InvalidOperationException("Injected boundary interruption.")) : Task.CompletedTask;
    }
    private sealed class TestJournal : IDriverEnforcementJournal
    {
        public bool Available { get; set; } = true;
        public Dictionary<Guid, DriverLifecycleIntent> Intents { get; } = [];
        private Dictionary<Guid, long> Reconciled { get; } = [];
        public Task<DriverJournalState> ReadAsync(DriverScope scope, CancellationToken ct = default) =>
            Task.FromResult(new DriverJournalState(Available, Reconciled.Values.DefaultIfEmpty().Max(),
                Intents.Values.Where(i => i.Scope == scope && !Reconciled.ContainsKey(i.OperationId)).ToList()));
        public Task<DriverLifecycleIntent> AppendAsync(DriverLifecycleIntent intent, CancellationToken ct = default)
        {
            if (!Available) throw new InvalidOperationException("Journal unavailable.");
            if (!Intents.TryGetValue(intent.OperationId, out var previous)) Intents.Add(intent.OperationId, previous = intent);
            if (previous.Scope != intent.Scope || previous.GrantId != intent.GrantId || previous.Kind != intent.Kind)
                throw new InvalidOperationException("Stable operation identity conflict.");
            return Task.FromResult(previous);
        }
        public Task ReconcileAsync(DriverLifecycleIntent intent, long revision, CancellationToken ct = default)
        { Reconciled[intent.OperationId] = revision; return Task.CompletedTask; }
    }
}
