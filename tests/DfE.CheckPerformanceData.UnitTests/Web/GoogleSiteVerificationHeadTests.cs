using DfE.CheckPerformanceData.Web.Settings;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Google Search Console verifies ownership of a host by reading a
// <meta name="google-site-verification"> tag from its home page. The token is issued per
// property, so each environment carries its own in terraform/application/config/{env}.yml
// (GoogleSiteVerification__Content) and an environment with no token renders no tag.
// Verification is what unlocks the Removals tool, which is how the QA host is taken out of
// Google's results (#444).
//
// Static Razor-source assertions, same pattern as LayoutRenderTests.
public sealed class GoogleSiteVerificationHeadTests
{
	private static string ReadView(string name)
	{
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var view = Path.Combine(repoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", name);
		return File.ReadAllText(view);
	}

	private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
		=> path;

	[Fact]
	public void Settings_DefaultToNoToken()
	{
		Assert.Equal(string.Empty, new GoogleSiteVerificationSettings().Content);
	}

	[Fact]
	public void Partial_RendersTheMetaTagFromSettings()
	{
		var view = ReadView("_GoogleSiteVerificationHead.cshtml");

		Assert.Contains("IOptions<GoogleSiteVerificationSettings>", view);
		Assert.Contains("<meta name=\"google-site-verification\" content=\"@", view);
	}

	// An environment with no token must not emit an empty tag: Google treats an empty content
	// as a failed verification rather than as absent, and it is noise in every other host's head.
	[Fact]
	public void Partial_RendersNothingWhenNoTokenIsConfigured()
	{
		var view = ReadView("_GoogleSiteVerificationHead.cshtml");

		Assert.Contains("string.IsNullOrWhiteSpace", view);
	}

	[Fact]
	public void Layout_IncludesThePartialInHead()
	{
		var layout = ReadView("_Layout.cshtml");

		var head = layout.IndexOf("<head>", StringComparison.Ordinal);
		var headEnd = layout.IndexOf("</head>", StringComparison.Ordinal);
		var include = layout.IndexOf("_GoogleSiteVerificationHead", StringComparison.Ordinal);

		Assert.True(head >= 0 && headEnd > head, "layout has a <head> block");
		Assert.InRange(include, head, headEnd);
	}
}
