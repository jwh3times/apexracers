namespace ApexRacers.Api.Middleware;

/// <summary>Resource permissions for the built SPA and the Development-only API reference.</summary>
public static class ContentSecurityPolicy
{
    private const string Restrictions = "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public const string Spa = "default-src 'self'; script-src 'self'; style-src 'self'; " +
        "img-src 'self' data: https://images-static.iracing.com; font-src 'self'; connect-src 'self'; " +
        Restrictions;

    // Called only by the mapped Development Scalar HTML handler, never by matching a URL prefix.
    // Scalar serves its bundled JS locally. Its pinned bundle injects additional style elements
    // without propagating the nonce, so only this Development document allows inline CSS.
    // Scripts still require the request nonce or the same origin; eval remains blocked.
    public static string ForScalar(string nonce) =>
        $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
        Restrictions;
}
