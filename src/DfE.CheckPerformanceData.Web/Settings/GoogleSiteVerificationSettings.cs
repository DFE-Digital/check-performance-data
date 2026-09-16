namespace DfE.CheckPerformanceData.Web.Settings;

/// <summary>
/// The Google Search Console ownership token for this host, rendered as a
/// <c>google-site-verification</c> meta tag by <c>_GoogleSiteVerificationHead.cshtml</c>.
/// Issued per property, so each environment sets its own in
/// <c>terraform/application/config/{env}.yml</c> (<c>GoogleSiteVerification__Content</c>).
/// Empty means no tag. It is not a secret: it proves nothing on its own and is visible in the
/// page source of every verified site.
/// </summary>
public record GoogleSiteVerificationSettings
{
    public string Content { get; init; } = string.Empty;
}
