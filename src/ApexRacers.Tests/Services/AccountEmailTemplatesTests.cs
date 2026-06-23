using ApexRacers.Api.Services.Email;
using Xunit;

namespace ApexRacers.Tests.Services;

public class AccountEmailTemplatesTests
{
    [Fact]
    public void PasswordReset_SetsRecipientSubjectAndEmbedsUrl()
    {
        var email = AccountEmailTemplates.PasswordReset("driver@example.com", "https://apexracers.gg/reset-password?token=abc%20123");

        Assert.Equal("driver@example.com", email.To);
        Assert.Equal("Reset your ApexRacers password", email.Subject);
        Assert.Contains("https://apexracers.gg/reset-password?token=abc%20123", email.HtmlBody);
        Assert.Contains("https://apexracers.gg/reset-password?token=abc%20123", email.TextBody);
        Assert.Contains("ignore", email.TextBody, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmailChangeVerification_SetsRecipientSubjectAndEmbedsUrl()
    {
        var email = AccountEmailTemplates.EmailChangeVerification("new@example.com", "https://apexracers.gg/verify-email?token=xyz");

        Assert.Equal("new@example.com", email.To);
        Assert.Equal("Confirm your new ApexRacers email", email.Subject);
        Assert.Contains("https://apexracers.gg/verify-email?token=xyz", email.HtmlBody);
        Assert.Contains("https://apexracers.gg/verify-email?token=xyz", email.TextBody);
    }

    [Fact]
    public void EmailChangeNotice_GoesToOldAddressAndNamesNewEmailPlusSecurityLink()
    {
        var email = AccountEmailTemplates.EmailChangeNotice(
            "old@example.com", "new@example.com", "https://apexracers.gg/forgot-password");

        Assert.Equal("old@example.com", email.To);
        Assert.Contains("new@example.com", email.HtmlBody);
        Assert.Contains("new@example.com", email.TextBody);
        Assert.Contains("https://apexracers.gg/forgot-password", email.HtmlBody);
        Assert.Contains("https://apexracers.gg/forgot-password", email.TextBody);
    }

    [Fact]
    public void EmailChangeNotice_HtmlEncodesTheRequestedAddress()
    {
        var email = AccountEmailTemplates.EmailChangeNotice(
            "old@example.com", "<script>x</script>@evil.com", "https://apexracers.gg/forgot-password");

        Assert.DoesNotContain("<script>", email.HtmlBody);
        Assert.Contains("&lt;script&gt;", email.HtmlBody);
    }
}
