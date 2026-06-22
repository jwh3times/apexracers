namespace ApexRacers.Api.Services.Email;

/// <summary>
/// Provider-agnostic outbound email. ACS SDK types never leak past <see cref="IEmailSender"/>.
/// </summary>
public record OutboundEmail(string To, string? ToName, string Subject, string HtmlBody, string TextBody);
