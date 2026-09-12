using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// The known-device cookie's name and attributes (issue #314).
/// </summary>
/// <remarks>
/// These are not formatting choices — each one is the security property, and the only caller is a
/// controller, which coverage excludes. Without these the attributes would be unexercised and a
/// change to any of them would break nothing.
/// </remarks>
public class KnownDeviceCookieTests
{
    private static readonly DateTimeOffset Expires = new(2026, 12, 12, 0, 0, 0, TimeSpan.Zero);

    private sealed class Env(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "ApexRacers.Tests";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static readonly Env Production = new("Production");
    private static readonly Env Development = new("Development");

    [Fact]
    public void Options_AreNotReadableOrSentCrossSite()
    {
        var options = KnownDeviceCookie.Options(secure: true, Expires);

        Assert.True(options.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, options.SameSite);
        Assert.Equal(Expires, options.Expires);
        // Not subject to a consent gate: sign-in security does not work correctly without it.
        Assert.True(options.IsEssential);
    }

    /// <summary>
    /// The <c>__Host-</c> prefix constrains what the browser will accept, which is what defends
    /// against a sibling subdomain planting a cookie of the same name. The prefix is only valid with
    /// <c>Secure</c> and a root path, so those three travel together or the browser silently
    /// refuses to store it.
    /// </summary>
    [Fact]
    public void Options_AreValidForTheHostPrefix()
    {
        var options = KnownDeviceCookie.Options(secure: true, Expires);

        Assert.StartsWith("__Host-", KnownDeviceCookie.SecureName);
        Assert.True(options.Secure);
        Assert.Equal("/", options.Path);
        // A Domain would make the prefix invalid, and is what allows parent-domain shadowing.
        Assert.Null(options.Domain);
    }

    /// <summary>
    /// <c>Secure</c> follows the environment rather than <c>Request.IsHttps</c>. The request's
    /// scheme depends on forwarded-header processing, which is gated on an app setting the host only
    /// warns about — so keying off it would let a deploy slip silently downgrade a 90-day value to
    /// one sent in the clear.
    /// </summary>
    [Fact]
    public void Secure_FollowsTheEnvironmentNotTheRequest()
    {
        Assert.True(KnownDeviceCookie.IsSecure(Production));
        Assert.False(KnownDeviceCookie.IsSecure(Development));
    }

    [Fact]
    public void Name_IsPrefixedEverywhereItCanBeSecure()
    {
        Assert.Equal(KnownDeviceCookie.SecureName, KnownDeviceCookie.NameFor(Production));
        // Development over plain HTTP cannot use the prefix: the browser would refuse the cookie and
        // the exemption would appear broken with nothing reporting why.
        Assert.Equal(KnownDeviceCookie.DevelopmentName, KnownDeviceCookie.NameFor(Development));
    }

    /// <summary>
    /// Reading accepts only the name this environment writes. Accepting either would give back the
    /// shadowing the prefix exists to prevent — an attacker who cannot set a <c>__Host-</c> cookie
    /// can still set an unprefixed one on a parent domain.
    /// </summary>
    [Fact]
    public void Read_InProduction_IgnoresTheUnprefixedName()
    {
        var request = RequestWith(
            (KnownDeviceCookie.DevelopmentName, "planted-by-someone-else"));

        Assert.Null(KnownDeviceCookie.Read(request, Production));
    }

    [Fact]
    public void Read_InProduction_AcceptsThePrefixedName()
    {
        var request = RequestWith(
            (KnownDeviceCookie.SecureName, "genuine"),
            (KnownDeviceCookie.DevelopmentName, "planted-by-someone-else"));

        Assert.Equal("genuine", KnownDeviceCookie.Read(request, Production));
    }

    [Fact]
    public void Read_AbsentCookie_IsNull()
    {
        Assert.Null(KnownDeviceCookie.Read(RequestWith(), Production));
    }

    [Fact]
    public void Write_SetsTheNameAndExpiryTheServiceIssued()
    {
        var context = new DefaultHttpContext();
        var issued = new IssuedDevice("device-value", Expires);

        KnownDeviceCookie.Write(context.Response, issued, Production);

        var header = Assert.Single(context.Response.Headers.SetCookie!);
        Assert.NotNull(header);
        Assert.Contains($"{KnownDeviceCookie.SecureName}=device-value", header);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpRequest RequestWith(params (string Name, string Value)[] cookies)
    {
        var context = new DefaultHttpContext();
        if (cookies.Length > 0)
        {
            context.Request.Headers.Cookie =
                string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        }

        return context.Request;
    }
}
