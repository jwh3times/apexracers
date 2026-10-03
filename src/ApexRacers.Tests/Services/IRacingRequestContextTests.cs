using ApexRacers.Api.Services;
using ApexRacers.Api.Middleware;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Tests.Helpers;
using Xunit;
using Microsoft.AspNetCore.Http;

namespace ApexRacers.Tests.Services;

public class IRacingRequestContextTests
{
    [Fact]
    public async Task CallerHeaderCannotSelectPublishedDriverEvidenceNamespace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = DbContextFactory.Create();
        db.FeatureFlags.RemoveRange(db.FeatureFlags);
        db.FeatureFlags.Add(new FeatureFlag
        {
            Key = "iracing-demo", Name = "Demo", IsEnabled = true, MinimumRole = "Standard",
        });
        await db.SaveChangesAsync(ct);
        var scope = new IRacingDataScope();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/users/me/stats";
        context.Request.Headers["X-ApexRacers-Driver-Evidence-Namespace"] = "real";
        await new IRacingDataScopeMiddleware(_ => Task.CompletedTask).InvokeAsync(context,
            new IRacingRequestContext(new FeatureFlagEligibility(db), scope));
        Assert.Equal(DataProvenance.Demo, scope.Provenance);
        Assert.Equal("demo", context.Response.Headers["X-ApexRacers-Driver-Evidence-Namespace"]);
    }

    [Theory]
    [InlineData(true, true, DataProvenance.Demo)]
    [InlineData(true, false, DataProvenance.Demo)]
    [InlineData(false, true, DataProvenance.Real)]
    [InlineData(false, false, DataProvenance.Unknown)]
    public async Task ServerFlagsSelectScopeIndependentlyOfNumericDriverIds(
        bool demo, bool live, DataProvenance expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = DbContextFactory.Create();
        db.FeatureFlags.RemoveRange(db.FeatureFlags);
        db.FeatureFlags.AddRange(
            new FeatureFlag { Key = "iracing-demo", Name = "Demo", IsEnabled = demo, MinimumRole = "Standard" },
            new FeatureFlag { Key = "iracing-live", Name = "Live", IsEnabled = live, MinimumRole = "Standard" });
        await db.SaveChangesAsync(ct);
        var scope = new IRacingDataScope();
        var request = new IRacingRequestContext(new FeatureFlagEligibility(db), scope);
        Assert.Equal(expected, await request.SelectAsync(null, ct));
        Assert.Equal(expected, scope.Provenance);
        Assert.Throws<InvalidOperationException>(() => scope.Select(DataProvenance.Real));
    }
}
