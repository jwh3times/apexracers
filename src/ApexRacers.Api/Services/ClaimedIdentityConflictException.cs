namespace ApexRacers.Api.Services;

/// <summary>
/// Raised when a User attempts to assert a Claimed Identity already held by another User.
/// The message deliberately identifies no User or account.
/// </summary>
public sealed class ClaimedIdentityConflictException(Exception? innerException = null)
    : Exception(DefaultMessage, innerException)
{
    public const string DefaultMessage =
        "This iRacing Customer ID is already claimed by another account. " +
        "Check the Customer ID or contact support if you believe the claim is yours.";
}
