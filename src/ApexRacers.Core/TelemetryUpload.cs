namespace ApexRacers.Core;

/// <summary>
/// The size bound on one Telemetry Upload, authored once for every layer that has to agree on it.
/// </summary>
/// <remarks>
/// <para>
/// There are two bounds because they bound different things, and only one of them can produce a
/// message worth showing. <see cref="MaxFileSizeBytes"/> is the product rule — the number the upload
/// page advertises and the number the API refuses a file for. <see cref="MaxRequestBytes"/> is the
/// transport backstop the framework enforces while reading the body, and it has to sit
/// <em>above</em> the file bound: a multipart request carries a boundary marker and part headers on
/// top of the file's own bytes, so the request holding a file of exactly
/// <see cref="MaxFileSizeBytes"/> is necessarily larger than that. Setting the two equal would make
/// the file bound unreachable and hand every oversized upload to the framework instead.
/// </para>
/// <para>
/// That gap is what buys the clear answer. A file between the two bounds reaches the action and is
/// refused with a 413 naming the limit; only a request past the transport bound is cut off during
/// model binding, which answers 400 with "Failed to read the request form." (Verified against the
/// framework rather than assumed — the read failure is folded into model state, so it never reaches
/// <c>ExceptionHandlingMiddleware</c> and never becomes a 500.) That is the deliberate trade:
/// bounding how many bytes get buffered matters more than the wording once a caller is a whole
/// megabyte past what the page told them.
/// </para>
/// <para>
/// The upload page mirrors <see cref="MaxFileSizeMegabytes"/> in TypeScript
/// (<c>web/src/features/telemetry/uploadLimits.ts</c>) and checks a file against it before sending,
/// so the ordinary oversized case never spends the bandwidth at all. That mirror is the only place
/// this value is duplicated — change the two together, the same way
/// <c>purge_demo_data.sql</c> mirrors <c>DemoData.CacheSentinelThreshold</c>.
/// </para>
/// </remarks>
public static class TelemetryUpload
{
    /// <summary>
    /// The file bound in whole megabytes. The one number to change: both byte bounds and the
    /// message quoting the limit derive from it.
    /// </summary>
    public const int MaxFileSizeMegabytes = 250;

    /// <summary>The largest telemetry file a User may submit.</summary>
    public const long MaxFileSizeBytes = MaxFileSizeMegabytes * 1024L * 1024L;

    /// <summary>
    /// The largest request body the API will read — <see cref="MaxFileSizeBytes"/> plus a megabyte
    /// of headroom for multipart framing, so a file at exactly the file bound still arrives.
    /// </summary>
    public const long MaxRequestBytes = MaxFileSizeBytes + 1024L * 1024L;

    /// <summary>
    /// The refusal a caller sees when their file is over <see cref="MaxFileSizeBytes"/>. Quotes the
    /// limit from the constant so the wording cannot drift from what is enforced.
    /// </summary>
    // Not const: C# only folds an interpolation into a constant when every hole is itself a string
    // constant, and the megabyte bound is an int. Deriving it beats writing the number twice.
    public static readonly string TooLargeMessage =
        $"Telemetry file is too large. The limit is {MaxFileSizeMegabytes} MB per file.";
}
