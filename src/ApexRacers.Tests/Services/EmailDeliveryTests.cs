using ApexRacers.Api.Services.Email;
using Xunit;

namespace ApexRacers.Tests.Services;

public class EmailDeliveryTests
{
    [Fact]
    public void Select_NoMailDropAndNoAcs_UsesTheLogger()
    {
        Assert.Equal(EmailDeliveryMode.Log, EmailDelivery.Select(null, null, isDevelopment: true, "Development"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Select_BlankMailDropAndAcs_UsesAcs(string mailDropPath)
    {
        Assert.Equal(EmailDeliveryMode.Acs, EmailDelivery.Select(mailDropPath, "endpoint=https://acs;accesskey=k", isDevelopment: false, "Production"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Select_BlankMailDropIsNotConfigured(string mailDropPath)
    {
        // Whitespace must not count as "configured", or an empty env var in a deployed
        // environment would trip the Development-only guard and refuse to start.
        Assert.Equal(EmailDeliveryMode.Log, EmailDelivery.Select(mailDropPath, null, isDevelopment: false, "Production"));
    }

    [Fact]
    public void Select_MailDropInDevelopment_UsesTheFileDrop()
    {
        Assert.Equal(EmailDeliveryMode.FileDrop, EmailDelivery.Select("/app/mail", null, isDevelopment: true, "Development"));
    }

    [Fact]
    public void Select_MailDropWinsOverAcsInDevelopment()
    {
        // A stray ACS connection string must not silently divert a Development stack's reset
        // emails to a real provider — the E2E suite would then find an empty drop directory.
        Assert.Equal(
            EmailDeliveryMode.FileDrop,
            EmailDelivery.Select("/app/mail", "endpoint=https://acs;accesskey=k", isDevelopment: true, "Development"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Select_MailDropOutsideDevelopment_Throws(string environmentName)
    {
        // The drop writes live password-reset links to disk in cleartext. Failing startup keeps
        // that Development testing affordance from following the image into a deployed
        // environment (GHSA-qmqp-gxpr-867g).
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EmailDelivery.Select("/app/mail", null, isDevelopment: false, environmentName));

        Assert.Contains("DEV_MAIL_DROP_PATH", ex.Message);
        Assert.Contains(environmentName, ex.Message);
    }

    [Fact]
    public void Select_MailDropOutsideDevelopment_ThrowsEvenWhenAcsIsConfigured()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EmailDelivery.Select("/app/mail", "endpoint=https://acs;accesskey=k", isDevelopment: false, "Production"));
    }
}
