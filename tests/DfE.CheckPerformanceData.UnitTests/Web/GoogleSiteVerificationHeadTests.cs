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

	// Every layout sets Layout = "_GovUkPageTemplate", and that template owns the real
	// <html><head>. A literal <head> in a layout is not a head: the outer template drops it into
	// <body>, where the parser discards the tag and its contents become body markup. That is
	// where GTM, Clarity, the stylesheets and the verification tag were all landing on QA, so
	// Google never saw the tag. Head content must go through the template's Head section.
	[Theory]
	[InlineData("_Layout.cshtml")]
	[InlineData("_AdminLayout.cshtml")]
	[InlineData("_ShareLayout.cshtml")]
	public void Layouts_DoNotWriteTheirOwnHeadElement(string layout)
	{
		var view = ReadView(layout);

		Assert.DoesNotContain("<head>", view);
		Assert.DoesNotContain("</head>", view);
		Assert.Contains("@section Head", view);
	}

	[Fact]
	public void Layout_IncludesThePartialInTheHeadSection()
	{
		var layout = ReadView("_Layout.cshtml");

		var start = layout.IndexOf("@section Head", StringComparison.Ordinal);
		Assert.True(start >= 0, "layout defines @section Head");
		var end = layout.IndexOf("\n}", start, StringComparison.Ordinal);
		var include = layout.IndexOf("_GoogleSiteVerificationHead", StringComparison.Ordinal);

		Assert.InRange(include, start, end);
	}
}
