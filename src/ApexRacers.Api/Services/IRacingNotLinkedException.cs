namespace ApexRacers.Api.Services;

/// <summary>
/// Raised when an endpoint requires the caller's linked iRacing identity but none is available.
/// The exception middleware preserves <see cref="Code"/> as the typed 409 response contract used
/// by the web client.
/// </summary>
public sealed class IRacingNotLinkedException : Exception
{
    public const string Code = "IRACING_NOT_LINKED";
    public const string DefaultMessage = "Link your iRacing customer ID in Settings to view this data.";

    public IRacingNotLinkedException() : base(DefaultMessage)
    {
    }
}
