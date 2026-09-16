using DfE.CheckPerformanceData.Web.Settings;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The home page can carry a GOV.UK notification banner telling schools that this service is a
// beta and is not the existing checking-exercise site, with a link to that site. It is off by
// default and switched on per environment in terraform/application/config/{env}.yml
// (BetaBanner__Enabled); the sentence, link text and link target are also config so the
// wording can follow the exercise without a release.
//
// Static Razor-source assertions, same pattern as LayoutRenderTests.
public sealed class BetaBannerTests
{
	private static string ReadView(params string[] path)
	{
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var view = Path.Combine([repoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", .. path]);
		return File.ReadAllText(view);
	}

	private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
		=> path;

	[Fact]
	public void Settings_DefaultToHidden()
	{
		Assert.False(new BetaBannerSettings().Enabled);
	}

	[Fact]
	public void Settings_DefaultToTheKs2CheckingExerciseSite()
	{
		var settings = new BetaBannerSettings();

		Assert.Equal(
			"https://onlinecollections.des.fasst.org.uk/fastform/school-checking-exercise-ks2",
			settings.ExistingSiteUrl);
		Assert.False(string.IsNullOrWhiteSpace(settings.ExistingSiteLinkText));
		Assert.StartsWith("It is not the current site", settings.Message);
	}

	[Fact]
	public void Partial_IsGuardedOnEnabled()
	{
		var view = ReadView("Shared", "_BetaBanner.cshtml");

		Assert.Contains("IOptions<BetaBannerSettings>", view);
		Assert.Contains(".Enabled)", view);
	}

	[Fact]
	public void Partial_RendersAGovUkNotificationBannerFromSettings()
	{
		var view = ReadView("Shared", "_BetaBanner.cshtml");

		Assert.Contains("govuk-notification-banner", view);
		Assert.Contains("role=\"region\"", view);
		Assert.Contains("aria-labelledby=\"beta-banner-title\"", view);
		Assert.Contains("id=\"beta-banner-title\"", view);
		Assert.Contains("This is a beta version of a new service", view);
		Assert.Contains(".Message", view);
		Assert.Contains("href=\"@", view);
		Assert.Contains(".ExistingSiteUrl", view);
		Assert.Contains(".ExistingSiteLinkText", view);
	}

	[Fact]
	public void HomePage_IncludesTheBannerBeforeItsContent()
	{
		var view = ReadView("Home", "Index.cshtml");

		var banner = view.IndexOf("_BetaBanner", StringComparison.Ordinal);
		var content = view.IndexOf("govuk-grid-row", StringComparison.Ordinal);

		Assert.True(banner >= 0, "home page includes _BetaBanner");
		Assert.True(banner < content, "banner comes before the page content");
	}
}
