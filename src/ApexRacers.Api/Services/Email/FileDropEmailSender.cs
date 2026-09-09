using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ApexRacers.Api.Services.Email;

/// <summary>
/// One email as written to the drop directory. This is a file contract the E2E suite parses, so
/// treat its property names as stable.
/// </summary>
public record DroppedEmail(string To, string? ToName, string Subject, string HtmlBody, string TextBody, DateTimeOffset DroppedAt);

/// <summary>
/// Development-only sender that writes each outbound email to a local directory instead of
/// delivering it, so the password-reset and email-change flows stay testable end to end without an
/// email provider.
///
/// This replaces the former Development-only reset-token echo in the
/// <c>POST /api/auth/forgot-password</c> response body, which handed a live single-use credential
/// to any unauthenticated caller who knew an email address — full account takeover on a
/// Development instance that was reachable over the network (GHSA-qmqp-gxpr-867g). A directory on
/// the local filesystem has no HTTP surface, so exposing the instance leaks nothing through it, and
/// <see cref="EmailDelivery.Select"/> refuses to start outside Development.
/// </summary>
public sealed class FileDropEmailSender(string directory, TimeProvider timeProvider, ILogger<FileDropEmailSender> logger) : IEmailSender
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);

        var now = timeProvider.GetUtcNow();
        // Sortable timestamp so a reader can take the newest match, plus a random suffix because
        // parallel E2E workers can drop two emails inside the same millisecond.
        var fileName = $"{now:yyyyMMdd'T'HHmmss'.'fff'Z'}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(directory, fileName);

        var payload = new DroppedEmail(email.To, email.ToName, email.Subject, email.HtmlBody, email.TextBody, now);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, SerializerOptions), ct);

        // Metadata only. The file itself necessarily carries the link; the log must not, because
        // logs are shipped off the box and the link is a single-use credential.
        logger.LogInformation(
            "Email delivery not configured; wrote '{Subject}' to the Development mail drop as {FileName}.",
            email.Subject, fileName);
    }
}
