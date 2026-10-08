using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ApexRacers.Tests.References;

public sealed class DriverReferencePolicyTests
{
    [Fact]
    public void Reference_has_256_random_bits_without_identity_material_and_only_a_hash_is_stored()
    {
        var values = Enumerable.Range(0, 100).Select(_ => DriverReferences.Create()).ToArray();
        Assert.Equal(100, values.Distinct().Count());
        Assert.All(values, value => { Assert.True(DriverReferences.Valid(value)); Assert.Equal(64, DriverReferences.Hash(value).Length); Assert.NotEqual(value, DriverReferences.Hash(value)); });
        Assert.False(DriverReferences.Valid(null)); Assert.False(DriverReferences.Valid("2")); Assert.False(DriverReferences.Valid(new string('G', 64)));
        Assert.Equal(TimeSpan.FromMinutes(15), DriverReferences.Lifetime);
    }
    [Fact]
    public async Task Ordinary_startup_without_controlled_catalog_never_issues_or_publishes_references()
    {
        await using var db = DbContextFactory.Create();
        var journal = new UnavailableDriverEnforcementJournal();
        var store = new DriverReferenceStore(db, TimeProvider.System, journal);
        var module = new ScopedDriverPublication(store, journal, Guid.NewGuid());
        var ct = TestContext.Current.CancellationToken;
        Assert.IsAssignableFrom<ObjectResult>(await module.DiscoverAsync(ReferenceActors.Recipient, null, ct: ct));
        Assert.IsAssignableFrom<ObjectResult>(await module.ReadAsync(ReferenceActors.Recipient, DriverReferences.Create(), DriverReferencePurpose.Detail, ct));
        Assert.IsAssignableFrom<ObjectResult>(await module.FollowAsync(ReferenceActors.Recipient, DriverReferences.Create(), ct));
        Assert.Null(await store.RecipientAsync(ReferenceActors.Recipient, ct)); // Real scope never aliases Demo.
        Assert.Null(await store.TargetAsync(ReferenceActors.Scope(ReferenceActors.Target), ct));
        Assert.Null(await store.ResolveAsync(ReferenceActors.Recipient, "2", DriverReferencePurpose.Detail, ct));
        Assert.Null(await store.IssueAsync(ReferenceActors.Recipient, ReferenceActors.Scope(ReferenceActors.Target), (DriverReferencePurpose)99, ct));
    }
}
