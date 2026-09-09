using System.Text.RegularExpressions;
using System.Web;
using ApexRacers.Api.Services.Email;
using Xunit;

namespace ApexRacers.Tests.Helpers;

/// <summary>
/// Pulls the single-use token back out of an emailed account link. Tests read tokens the way a real
/// recipient does — out of the delivered message — because the service deliberately no longer
/// returns them to its caller (GHSA-qmqp-gxpr-867g). This mirrors what the E2E suite does with the
/// Development mail drop.
/// </summary>
public static class EmailLinks
{
    private static readonly Regex LinkPattern = new(@"https?://\S+?\?[^\s""'<>]+", RegexOptions.Compiled);

    /// <summary>Returns the <c>token</c> query value from the first link in the email's text body.</summary>
    public static string TokenFrom(OutboundEmail email)
    {
        var match = LinkPattern.Match(email.TextBody);
        Assert.True(match.Success, $"No link with a query string found in the '{email.Subject}' email body.");

        var token = HttpUtility.ParseQueryString(new Uri(match.Value).Query)["token"];
        Assert.False(string.IsNullOrEmpty(token), $"The '{email.Subject}' link carried no token: {match.Value}");
        return token!;
    }
}
