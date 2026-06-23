namespace ApexRacers.Api.Services.Email;

/// <summary>Pure builders for account-security emails (branded HTML + plain-text). Unit-tested directly.</summary>
public static class AccountEmailTemplates
{
    private const string BrandName = "ApexRacers";

    public static OutboundEmail PasswordReset(string toEmail, string resetUrl)
    {
        const string subject = "Reset your ApexRacers password";
        var html = Layout(
            "Reset your password",
            "We received a request to reset your ApexRacers password. Use the button below to choose a new one. " +
            "The link expires shortly and can be used once.",
            "Reset password", resetUrl,
            "If you didn't request this, you can safely ignore this email — your password won't change.");
        var text =
            $"Reset your {BrandName} password\n\n" +
            $"We received a request to reset your password. Open this link to choose a new one:\n{resetUrl}\n\n" +
            "If you didn't request this, ignore this email — your password won't change.";
        return new OutboundEmail(toEmail, null, subject, html, text);
    }

    public static OutboundEmail EmailChangeVerification(string toEmail, string verifyUrl)
    {
        const string subject = "Confirm your new ApexRacers email";
        var html = Layout(
            "Confirm your email change",
            "You asked to change the email on your ApexRacers account to this address. Use the button below to confirm. " +
            "Your email won't change until you do.",
            "Confirm email", verifyUrl,
            "If you didn't request this, you can safely ignore this email.");
        var text =
            $"Confirm your new {BrandName} email\n\n" +
            $"You asked to change your account email to this address. Open this link to confirm:\n{verifyUrl}\n\n" +
            "If you didn't request this, ignore this email.";
        return new OutboundEmail(toEmail, null, subject, html, text);
    }

    /// <summary>
    /// Security notice sent to the account's CURRENT (old) address when an email change is requested,
    /// so a hijacked-session change is detectable before it completes. The requested address is
    /// HTML-encoded since it is attacker-controllable in a takeover.
    /// </summary>
    public static OutboundEmail EmailChangeNotice(string toEmail, string newEmail, string securityUrl)
    {
        const string subject = "Security notice: an email change was requested";
        var encodedNew = System.Net.WebUtility.HtmlEncode(newEmail);
        var html = Layout(
            "Email change requested",
            $"A request was made to change the email on your ApexRacers account to <strong>{encodedNew}</strong>. " +
            "If this was you, follow the confirmation link sent to that new address — nothing to do here. " +
            "If this wasn't you, reset your password now to secure your account.",
            "Reset your password", securityUrl,
            "This notice was sent to the current address on your account.");
        var text =
            $"Security notice — {BrandName} email change requested\n\n" +
            $"A request was made to change your account email to {newEmail}. If this was you, follow the " +
            "confirmation link sent to that new address — nothing to do here.\n\n" +
            $"If this wasn't you, reset your password now to secure your account:\n{securityUrl}";
        return new OutboundEmail(toEmail, null, subject, html, text);
    }

    private static string Layout(string heading, string body, string cta, string url, string footnote) =>
        $$"""
        <!DOCTYPE html>
        <html>
          <body style="margin:0;background:#0b0f14;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#e6edf3;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#0b0f14;padding:32px 0;">
              <tr><td align="center">
                <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background:#11161d;border:1px solid #1f2730;border-radius:14px;overflow:hidden;">
                  <tr><td style="padding:24px 32px;border-bottom:1px solid #1f2730;font-size:18px;font-weight:700;color:#00e0ff;letter-spacing:-0.3px;">{{BrandName}}</td></tr>
                  <tr><td style="padding:32px;">
                    <h1 style="margin:0 0 16px;font-size:20px;color:#e6edf3;">{{heading}}</h1>
                    <p style="margin:0 0 24px;font-size:14px;line-height:1.6;color:#aab4c0;">{{body}}</p>
                    <a href="{{url}}" style="display:inline-block;background:#00e0ff;color:#04222a;font-weight:700;font-size:14px;text-decoration:none;padding:12px 24px;border-radius:10px;">{{cta}}</a>
                    <p style="margin:24px 0 0;font-size:12px;line-height:1.6;color:#6b7785;">Or paste this link into your browser:<br><span style="color:#9fb0c0;word-break:break-all;">{{url}}</span></p>
                    <p style="margin:24px 0 0;font-size:12px;line-height:1.6;color:#6b7785;">{{footnote}}</p>
                  </td></tr>
                </table>
              </td></tr>
            </table>
          </body>
        </html>
        """;
}
