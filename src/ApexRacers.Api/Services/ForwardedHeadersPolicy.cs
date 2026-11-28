using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace ApexRacers.Api.Services;

/// <summary>
/// Who the API is willing to believe about the client's address and scheme.
///
/// <para><b>Why this exists.</b> Per-IP rate limiting keys off <c>Connection.RemoteIpAddress</c>
/// and HSTS off <c>Request.IsHttps</c>, and behind a reverse proxy both are correct only because
/// the forwarded-headers middleware rewrote them. That trust used to be expressed entirely by the
/// <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c> app setting, leaving nothing in the repository to
/// review (GHSA-fq5w-frqr-6px2). <see cref="Configure"/> is now that decision, in code.</para>
///
/// <para><b>Registration deliberately stays with the host.</b> Two changes here look like
/// improvements and are both worse than leaving it alone:</para>
/// <list type="bullet">
///   <item><description>Calling <c>app.UseForwardedHeaders()</c> while the app setting is also set
///   registers the middleware <em>twice</em>, so two forwarded entries are consumed instead of one.
///   Given <c>X-Forwarded-For: &lt;forged&gt;, &lt;real&gt;</c> the second pass resolves the
///   caller's own forged value — creating the exact spoofing hole the advisory is about.</description></item>
///   <item><description>Registering it unconditionally <em>instead of</em> the app setting would
///   process forwarded headers in environments that have no front end — local Compose, CI — where
///   the peer is the client itself. With <see cref="ForwardedHeadersOptions.KnownProxies"/> cleared,
///   that hands any caller the address the rate limiter partitions on.</description></item>
/// </list>
///
/// <para>So the app setting keeps its job — asserting "there is a front end in front of me", a
/// deployment fact rather than a code one — and this owns what is then believed. If the setting is
/// ever dropped, forwarded headers stop being processed and every caller shares the front end's
/// address: over-restrictive, never spoofable. <see cref="IsEnabledByHost"/> exists so that state
/// is logged at startup instead of being discovered from a rate-limit anomaly.</para>
/// </summary>
public static class ForwardedHeadersPolicy
{
    /// <summary>The app setting by which the host registers the middleware for us.</summary>
    public const string EnabledVariable = "ASPNETCORE_FORWARDEDHEADERS_ENABLED";

    /// <summary>
    /// Whether the host will register the forwarded-headers middleware — i.e. whether
    /// <see cref="Configure"/>'s settings apply to anything. Parsed leniently because the value
    /// arrives from a deployment console, where a stray space is likelier than a typo.
    /// </summary>
    public static bool IsEnabledByHost(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The trust decision itself.
    ///
    /// <para><c>ForwardLimit = 1</c> is the load-bearing setting, and is set explicitly even though
    /// it is also the framework default: it means only the <em>rightmost</em> entry is read. On the
    /// warm path the platform front end appends the true client address last, so entries a caller
    /// invents to the left of it are never consulted — verified against production by sending a forged
    /// <c>X-Forwarded-For</c> and confirming the logged address was the real one. Widening this is
    /// what would let a caller pick its own rate-limit partition.</para>
    ///
    /// <para><b>One accepted exception: a request held through scale from zero.</b> When the API is
    /// at zero replicas, the request that wakes it reaches the app through an internal platform hop,
    /// and the client address the app resolves is that hop's (in the RFC 6598 range,
    /// <c>100.64.0.0/10</c>), whether the hop drops the header or appends itself. Such a request is
    /// logged and rate-limited as the hop. The scheme is unaffected: a cold response still carries the
    /// HSTS header, which only a request seen as HTTPS gets. A forged entry is still ignored on that
    /// path, so it is not a spoofing hole, only lost attribution. Each wake holds only the few requests
    /// that arrive during the start, so the hop's partitions stay far below every per-minute limit.
    /// That was verified against production by sending the same forged <c>X-Forwarded-For</c> to the
    /// app while it was at zero replicas and again while it was warm, and by checking the HSTS header
    /// on a cold response. Recovering the client on that path would mean
    /// trusting the hop's range and widening <c>ForwardLimit</c>, which reopens the spoofing question
    /// this class settles.</para>
    ///
    /// <para><c>KnownProxies</c> and <c>KnownNetworks</c> are cleared because the platform front
    /// end's address is neither fixed nor documented; the defaults trust only loopback, which would
    /// make the middleware a silent no-op. That clearing is precisely the assumption
    /// <c>ForwardLimit</c> guards, which is why the two belong in one place.</para>
    ///
    /// <para><c>XForwardedHost</c> is deliberately absent: the Host header feeds link generation and
    /// redirects, and nothing here needs the proxy's word for it.</para>
    /// </summary>
    public static void Configure(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
}
