using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// ForwardLimit is the security-relevant setting: it is what stops a caller prepending entries to
/// X-Forwarded-For and choosing the address the rate limiter partitions on. The host-enabled
/// predicate only drives a startup warning, but it decides whether any of this applies at all,
/// so both halves are pinned.
/// </summary>
public class ForwardedHeadersPolicyTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("  true  ")]
    public void RecognizesTheHostHavingRegisteredTheMiddleware(string enabled)
    {
        Assert.True(ForwardedHeadersPolicy.IsEnabledByHost(enabled));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("yes")]
    public void TreatsAnythingElseAsNotRegistered(string? enabled)
    {
        // Only the literal "true" enables it in the host, so anything else must read as off
        // here too - a warning that fails to fire is how a dropped setting stays invisible.
        Assert.False(ForwardedHeadersPolicy.IsEnabledByHost(enabled));
    }

    [Fact]
    public void OnlyTheRightmostForwardedEntryIsBelieved()
    {
        // ForwardLimit is the whole defence: the front end appends the true client address last,
        // so entries a caller invents to the left of it are never read. This is also the framework
        // default, and is set explicitly so a future default change cannot silently widen trust.
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersPolicy.Configure(options);

        Assert.Equal(1, options.ForwardLimit);
    }

    [Fact]
    public void TrustsOnlyTheAddressAndSchemeHeaders()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersPolicy.Configure(options);

        Assert.Equal(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
        // Not XForwardedHost: the Host header feeds link generation and redirects, and nothing
        // here needs the proxy's word for it.
        Assert.False(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }

    [Fact]
    public void ClearsTheDefaultKnownProxyAndNetwork()
    {
        // The defaults trust only loopback, which would make the middleware a no-op behind a
        // platform front end whose address is neither fixed nor documented. Clearing them is what
        // ForwardLimit = 1 is guarding.
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersPolicy.Configure(options);

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownNetworks);
    }
}
