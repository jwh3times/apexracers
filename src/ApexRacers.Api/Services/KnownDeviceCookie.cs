namespace ApexRacers.Api.Services;

/// <summary>
/// The one place the known-device cookie's name and attributes are decided, and the only place it is
/// read or written (issue #314). Kept out of the controller so the transport policy is stated once
/// and reviewed as a unit.
/// </summary>
/// <remarks>
/// <para>
/// This is the only cookie the API sets. It is deliberately not a session mechanism — sign-in still
/// answers with a JWT and a refresh token in the body, exactly as before — and it grants no access
/// on its own: it only selects which counter a sign-in attempt is throttled against. See
/// <see cref="ApexRacers.Core.Models.KnownDevice"/> for why that distinction is what lets it be
/// long-lived.
/// </para>
/// <para>
/// <b>HttpOnly</b> because no page script has any use for it, and a value script cannot read is a
/// value an injected script cannot steal. <b>SameSite=Strict</b> because the only request that ever
/// needs it is a same-origin sign-in from this app's own SPA, which the API serves from
/// <c>wwwroot</c>.
/// </para>
/// <para>
/// <b>The <c>__Host-</c> prefix is load-bearing, and not for the reason it first appears.</b>
/// Omitting <c>Domain</c> controls what <em>this server sets</em>; the prefix controls what the
/// browser is willing to <em>store and send back</em>. A browser refuses a <c>__Host-</c>-named
/// cookie that carries a <c>Domain</c>, arrived over plain HTTP, or has a non-root <c>Path</c> —
/// which is what makes it defend against a <em>different party</em> writing a cookie of the same
/// name. Cookies are not origin-isolated: anything able to set a cookie on the registrable parent
/// domain (a compromised sibling subdomain, or an active network attacker against any plain-HTTP
/// host under it — a <c>Secure</c> cookie can still be overwritten from an insecure origin) could
/// otherwise plant <c>apexracers_device=…; Domain=&lt;parent&gt;</c>. The browser would then send two
/// cookies of that name and which one wins is decided by header ordering this application does not
/// control. The damage would be quiet: the Driver's real cookie is shadowed, the exemption silently
/// never applies, and because a renewal only happens when the presented value resolves to this
/// account, the victim would mint a fresh row on every sign-in and churn through the per-account
/// device cap, evicting all of their genuine devices. The production deployment is a custom apex
/// domain, so this is reachable rather than theoretical. The prefix costs a root
/// <c>Path</c> — the cookie rides along on more same-origin requests — which is a far smaller price,
/// given it is <c>HttpOnly</c> and authorises nothing.
/// </para>
/// <para>
/// <b>Reading is restricted to the name this environment writes</b>, never "either name". Accepting
/// the unprefixed name in production would hand back exactly the shadowing the prefix exists to
/// prevent: an attacker who cannot plant a <c>__Host-</c> cookie can still plant an unprefixed one.
/// </para>
/// <para>
/// <b><c>Secure</c> follows the environment, not <c>Request.IsHttps</c>.</b> That property reflects
/// the client's scheme only once forwarded headers are processed, and that registration is gated on
/// an app setting whose absence the host merely warns about. Keying off the request would mean a
/// dropped setting silently downgraded a 90-day value to one sent over plain HTTP and overwritable
/// by a network attacker. The environment cannot flip under a deploy slip in the same way, and it
/// still lets a Development host on plain HTTP work — which is the only reason not to hard-code it
/// true.
/// </para>
/// </remarks>
public static class KnownDeviceCookie
{
    /// <summary>Name used wherever the cookie can be marked <c>Secure</c> — i.e. every real deployment.</summary>
    public const string SecureName = "__Host-apexracers_device";

    /// <summary>
    /// Name used only in Development over plain HTTP, where a <c>__Host-</c> cookie would be refused
    /// by the browser and the exemption would appear broken for reasons nothing reports.
    /// </summary>
    public const string DevelopmentName = "apexracers_device";

    /// <summary>Whether this environment can set a <c>Secure</c> cookie.</summary>
    public static bool IsSecure(IWebHostEnvironment env) => !env.IsDevelopment();

    /// <summary>The cookie name this environment reads and writes.</summary>
    public static string NameFor(IWebHostEnvironment env) =>
        IsSecure(env) ? SecureName : DevelopmentName;

    /// <summary>
    /// The value the caller presented, or null. Reads only <see cref="NameFor"/> — see the remarks
    /// on why accepting the other name would undo the prefix.
    /// </summary>
    public static string? Read(HttpRequest request, IWebHostEnvironment env) =>
        request.Cookies[NameFor(env)];

    /// <summary>Sets the cookie for a browser that has just signed in successfully.</summary>
    public static void Write(HttpResponse response, IssuedDevice issued, IWebHostEnvironment env) =>
        response.Cookies.Append(NameFor(env), issued.Token, Options(IsSecure(env), issued.ExpiresAt));

    /// <summary>Attributes for writing the cookie. Exposed so the policy can be asserted directly.</summary>
    public static CookieOptions Options(bool secure, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Strict,
        // Root path is required by the __Host- prefix, and is why the prefix is worth its cost.
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}
