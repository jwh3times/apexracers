using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ApexRacers.Api.Services.Email;

/// <summary>Sends email via Azure Communication Services. Thin SDK glue — excluded from coverage.</summary>
[ExcludeFromCodeCoverage]
public sealed class AcsEmailSender(EmailClient client, IConfiguration config, ILogger<AcsEmailSender> logger)
    : IEmailSender
{
    private string SenderAddress => config["ACS_SENDER_ADDRESS"] ?? "noreply@apexracers.gg";

    public async Task SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        var content = new EmailContent(email.Subject) { PlainText = email.TextBody, Html = email.HtmlBody };
        var recipient = email.ToName is null
            ? new EmailAddress(email.To)
            : new EmailAddress(email.To, email.ToName);
        var message = new EmailMessage(SenderAddress, new EmailRecipients([recipient]), content);

        try
        {
            var op = await client.SendAsync(WaitUntil.Completed, message, ct);
            logger.LogInformation("Email sent (subject {Subject}); status {Status}", email.Subject, op.Value.Status);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "ACS email send failed (subject {Subject}); error {Code}", email.Subject, ex.ErrorCode);
            throw;
        }
    }
}
