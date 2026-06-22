using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace ApexRacers.Api.Services.Email;

/// <summary>Fallback used when ACS is unconfigured. Logs metadata only — never the link/token.</summary>
[ExcludeFromCodeCoverage]
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        logger.LogWarning(
            "Email delivery not configured (ACS_CONNECTION_STRING missing). Would have sent '{Subject}'.",
            email.Subject);
        return Task.CompletedTask;
    }
}
