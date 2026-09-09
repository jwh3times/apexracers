namespace ApexRacers.Api.Services.Email;

/// <summary>Which <see cref="IEmailSender"/> a given configuration selects.</summary>
public enum EmailDeliveryMode
{
    /// <summary>Write each email to a local directory — Development testing only.</summary>
    FileDrop,

    /// <summary>Deliver through Azure Communication Services.</summary>
    Acs,

    /// <summary>No delivery configured; log metadata only.</summary>
    Log,
}

/// <summary>
/// Pure configuration decision behind the <see cref="IEmailSender"/> registration in Program.cs.
/// Extracted so the Development-only guard on the file drop is directly testable rather than
/// living in the startup shell.
/// </summary>
public static class EmailDelivery
{
    /// <summary>
    /// Picks a delivery mode. The file drop wins over ACS when both are configured, so a
    /// Development stack exercising the reset flow is never silently switched to a real provider
    /// by a stray connection string.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A mail drop path is configured outside Development. The drop writes live password-reset and
    /// email-verification links to disk in cleartext; refusing to start keeps that testing
    /// affordance from following the image into a deployed environment.
    /// </exception>
    public static EmailDeliveryMode Select(string? mailDropPath, string? acsConnectionString, bool isDevelopment, string environmentName)
    {
        if (!string.IsNullOrWhiteSpace(mailDropPath))
        {
            if (!isDevelopment)
                throw new InvalidOperationException(
                    $"DEV_MAIL_DROP_PATH is a Development-only testing affordance and cannot be used in the '{environmentName}' " +
                    "environment: it writes live password-reset links to disk in cleartext. Unset it, and set " +
                    "ACS_CONNECTION_STRING to deliver email for real.");

            return EmailDeliveryMode.FileDrop;
        }

        return string.IsNullOrWhiteSpace(acsConnectionString) ? EmailDeliveryMode.Log : EmailDeliveryMode.Acs;
    }
}
