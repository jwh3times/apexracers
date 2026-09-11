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

    /// <summary>
    /// Sent when an account is created. The link is what makes the account usable — registration
    /// deliberately hands the caller nothing, so this email is the only way in.
    /// </summary>
    public static OutboundEmail EmailConfirmation(string toEmail, string confirmUrl)
    {
        const string subject = "Confirm your ApexRacers email";
        var html = Layout(
            "Confirm your email",
            "Welcome to ApexRacers. Confirm this address with the button below and you can sign in. " +
            "Until you do, the account stays inactive.",
            "Confirm email", confirmUrl,
            "If you didn't create this account, you can safely ignore this email — nothing will be activated.");
        var text =
            $"Confirm your {BrandName} email\n\n" +
            $"Welcome to {BrandName}. Open this link to confirm this address and sign in:\n{confirmUrl}\n\n" +
            "Until you do, the account stays inactive. If you didn't create it, ignore this email.";
        return new OutboundEmail(toEmail, null, subject, html, text);
    }

    /// <summary>
    /// Sent to the owner of an address someone just tried to register, when that account already
    /// exists and is confirmed. Registration itself stays silent about the address being taken, so
    /// this email is where that fact is disclosed — to the mailbox, which is the only place it belongs.
    /// </summary>
    public static OutboundEmail DuplicateRegistration(string toEmail, string resetUrl)
    {
        const string subject = "Someone tried to create an ApexRacers account with your email";
        var html = Layout(
            "You already have an account",
            "Someone just tried to sign up for ApexRacers with this address, which already has an account. " +
            "No second account was created and nothing has changed. If it was you, sign in as usual — " +
            "and if you've forgotten your password, reset it below.",
            "Reset your password", resetUrl,
            "If this wasn't you, no action is needed; whoever tried was not told that this address is registered.");
        var text =
            $"You already have a {BrandName} account\n\n" +
            "Someone just tried to sign up with this address, which already has an account. No second " +
            "account was created and nothing has changed.\n\n" +
            $"If it was you, sign in as usual. Forgotten your password? Reset it here:\n{resetUrl}\n\n" +
            "If this wasn't you, no action is needed.";
        return new OutboundEmail(toEmail, null, subject, html, text);
    }

    /// <summary>
    /// Sent when repeated failed sign-ins against an account stop looking like a typo. The sign-in
    /// response itself stays generic — saying anything there would tell whoever is guessing how far
    /// they had got — so this email is the only place it is disclosed, and it goes to the one party
    /// entitled to know.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wording deliberately does not say the account is locked, because since issue #300 it is
    /// not. Failures are held against the machine that produced them, so the owner can still sign in
    /// normally from their own; telling them to "wait a few minutes" would send them away from a
    /// door that is open. What they need to know is that someone is guessing, and that a password
    /// worth guessing is worth changing.
    /// </para>
    /// <para>
    /// Paced by <c>SignInAccountFailure.NoticeSentAt</c> to one message per account per interval.
    /// Sign-in is unauthenticated, so an email per lockout would be an inbox a stranger could fill.
    /// </para>
    /// </remarks>
    public static OutboundEmail SuspiciousSignInAttempts(string toEmail, string resetUrl)
    {
        const string subject = "Failed sign-in attempts on your ApexRacers account";
        var html = Layout(
            "Someone is trying to sign in",
            "We have blocked repeated failed sign-in attempts on your ApexRacers account. Nothing "
            + "about the account has changed, and you can still sign in as usual from your own "
            + "device — the attempts are blocked where they came from, not for you.",
            "Reset your password", resetUrl,
            "If this was you, no action is needed. If it wasn't, someone is guessing your password — "
            + "changing it now is the safest response.");
        var text =
            $"Failed sign-in attempts on your {BrandName} account\n\n" +
            "We have blocked repeated failed sign-in attempts on your account. Nothing about the " +
            "account has changed, and you can still sign in as usual from your own device — the " +
            "attempts are blocked where they came from, not for you.\n\n" +
            $"If it wasn't you, someone is guessing your password. Change it here:\n{resetUrl}\n\n" +
            "If this was you, no action is needed.";
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
