using ApexRacers.Core.PercentileHistory;
using ApexRacers.Api.Services;
using ApexRacers.Api.Services.PercentileHistory;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public sealed class PercentileHistoryOwnerTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DemoResolutionUsesSyntheticSubjectNotStoredClaimAndIsNeverPersonalProof()
    {
        await using var db = await CreateAsync(DataProvenance.Demo);
        var userId = await UserAsync(db, 777);
        db.FeatureFlags.Add(new FeatureFlag { Key = "iracing-demo", Name = "Demo", IsEnabled = true, MinimumRole = "Standard" });
        await db.SaveChangesAsync(Ct);
        var resolver = Resolver(db);
        var owner = Assert.IsType<PercentileHistoryOwner>(await resolver.ResolveDemoAsync(userId, Ct));
        Assert.Equal(userId, owner.Identity.UserId);
        Assert.Equal(DemoData.DriverCustId, owner.Identity.CustomerId);
        Assert.Equal(DataProvenance.Demo, owner.Identity.Provenance);
        Assert.Equal(PercentileOwnerKind.DemoSubject, owner.Identity.Kind);
        Assert.Null(owner.GrantId);
        Assert.Null(owner.Revision);
        Assert.False(owner.IsSubjectDriver(777));
        Assert.True(owner.IsSubjectDriver(DemoData.DriverCustId));
        Assert.Null(await resolver.ResolvePersonalAsync(userId, Ct));
        Assert.Null(await resolver.ResolveDemoAsync(Guid.NewGuid(), Ct));
    }

    [Theory]
    [InlineData(DataProvenance.Real)]
    [InlineData(DataProvenance.Unknown)]
    public async Task ClaimsAndDemoFlagDoNotCrossSelectedNamespaceOrInventPersonalAccess(DataProvenance provenance)
    {
        await using var db = await CreateAsync(provenance);
        var userId = await UserAsync(db, DemoData.DriverCustId);
        db.FeatureFlags.Add(new FeatureFlag { Key = "iracing-demo", Name = "Demo", IsEnabled = true, MinimumRole = "Standard" });
        await db.SaveChangesAsync(Ct);
        Assert.Null(await Resolver(db).ResolveDemoAsync(userId, Ct));
        Assert.Null(await Resolver(db).ResolvePersonalAsync(userId, Ct));
    }

    [Fact]
    public async Task DemoWithoutCurrentEligibilityDoesNotResolve()
    {
        await using var db = await CreateAsync(DataProvenance.Demo);
        var userId = await UserAsync(db, 777);
        Assert.Null(await Resolver(db).ResolveDemoAsync(userId, Ct));
    }

    [Fact]
    public async Task PersonalResolutionUsesCurrentVerifiedBindingAndVetoesLossOrUnavailableJournal()
    {
        await using var db = await CreateAsync(DataProvenance.Demo);
        var userId = await UserAsync(db, 777);
        var otherUser = await UserAsync(db, 778);
        var scope = new DriverScope(userId, 123456, DataProvenance.Demo);
        var journal = new ControlledJournal();
        var store = new DriverAuthorityStore(db, TimeProvider.System);
        var authority = new DriverAuthorization(store, journal, new ControlledProof(scope), TimeProvider.System);
        var grant = await authority.GrantAsync(scope, new(DriverAuthorizationPolicy.PersonalConsentVersion), Ct);
        var resolver = Resolver(db, authority);
        var owner = Assert.IsType<PercentileHistoryOwner>(await resolver.ResolvePersonalAsync(userId, Ct));
        Assert.Equal(123456, owner.Identity.CustomerId);
        Assert.Equal(PercentileOwnerKind.Personal, owner.Identity.Kind);
        Assert.Equal(grant.GrantId, owner.GrantId);
        Assert.Equal(grant.Revision, owner.Revision);
        Assert.False(owner.IsSubjectDriver(777));
        Assert.Null(await resolver.ResolvePersonalAsync(otherUser, Ct));
        journal.Available = false;
        Assert.Null(await resolver.ResolvePersonalAsync(userId, Ct));
        journal.Available = true;
        await authority.TransitionAsync(scope, DriverLifecycleKind.WithdrawPersonal, Guid.NewGuid(), Ct);
        Assert.Null(await resolver.ResolvePersonalAsync(userId, Ct));
        // The earlier snapshot remains data; re-resolution, not possession, determines access.
        Assert.Equal(grant.Revision, owner.Revision);
    }

    [Fact]
    public async Task OrdinaryUnavailableAdaptersCannotTurnRetainedBindingIntoReadAuthority()
    {
        await using var db = await CreateAsync(DataProvenance.Demo);
        var userId = await UserAsync(db, 777);
        var scope = new DriverScope(userId, 123456, DataProvenance.Demo);
        var store = new DriverAuthorityStore(db, TimeProvider.System);
        var controlled = new DriverAuthorization(store, new ControlledJournal(), new ControlledProof(scope), TimeProvider.System);
        await controlled.GrantAsync(scope, new(DriverAuthorizationPolicy.PersonalConsentVersion), Ct);
        Assert.Null(await Resolver(db).ResolvePersonalAsync(userId, Ct));
    }

    private async Task<AppDbContext> CreateAsync(DataProvenance provenance) =>
        new(await postgres.CreateOptionsAsync(Ct), new IRacingDataScope(provenance));

    private static async Task<Guid> UserAsync(AppDbContext db, long claim)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = id, UserName = $"synthetic-{id:N}", DisplayName = "Synthetic User",
            EmailConfirmed = true, IRacingCustomerId = claim });
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private static PercentileHistoryOwnerResolver Resolver(AppDbContext db, DriverAuthorization? authority = null) =>
        new(db, new SubjectDriverContext(db, new FeatureFlagEligibility(db)), authority ?? new DriverAuthorization(
            new DriverAuthorityStore(db, TimeProvider.System), new UnavailableDriverEnforcementJournal(),
            new UnavailableDriverOwnershipProof(), TimeProvider.System));

    private sealed class ControlledProof(DriverScope scope) : IDriverOwnershipProof
    {
        public Task<VerifiedDriverProof?> VerifyAsync(DriverScope requested, CancellationToken ct = default) =>
            Task.FromResult<VerifiedDriverProof?>(requested == scope
                ? new(Guid.NewGuid(), scope, DateTimeOffset.UtcNow.AddMinutes(-1), "synthetic-test-only", "Synthetic Driver") : null);
    }

    private sealed class ControlledJournal : IDriverEnforcementJournal
    {
        public bool Available { get; set; } = true;
        private List<DriverLifecycleIntent> Intents { get; } = [];
        public Task<DriverUserEnforcement> ReadUserAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(new DriverUserEnforcement(Available));
        public Task<DriverJournalState> ReadAsync(DriverScope scope, CancellationToken ct = default) =>
            Task.FromResult(new DriverJournalState(Available, 0, Intents.Where(i => i.Scope == scope).ToList()));
        public Task<DriverLifecycleIntent> AppendAsync(DriverLifecycleIntent intent, CancellationToken ct = default)
        { Intents.Add(intent); return Task.FromResult(intent); }
        public Task ReconcileAsync(DriverLifecycleIntent intent, long revision, CancellationToken ct = default) => Task.CompletedTask;
    }
}
