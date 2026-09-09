namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Static Razor-source assertions for the fixes raised by the Zoonou accessibility audit
// (epic #384) and for other view-level accessibility defects fixed since (#408). Same
// hostless pattern as LayoutRenderTests: read the .cshtml as text and
// assert on the source, so the suite needs no MVC test harness.
//
// Each fact names the ticket it pins. These are all defects that were found once and
// fixed once — the point of the test is that the markup cannot quietly regress, because
// nothing else in the build would notice a dropped attribute or a moved element.
public sealed class AccessibilityAuditViewTests
{
	private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
		=> path;

	private static string ReadView(params string[] viewPathSegments)
	{
		// Repo root is three levels up from tests/DfE.CheckPerformanceData.UnitTests/Web/.
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
		var segments = new[] { repoRoot, "src", "DfE.CheckPerformanceData.Web" }
			.Concat(viewPathSegments).ToArray();
		return File.ReadAllText(Path.Combine(segments));
	}

	private static string ReadCss()
	{
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
		return File.ReadAllText(Path.Combine(repoRoot, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "css", "site.css"));
	}

	// ── #374 Page regions not identified with ARIA landmarks ──────────────────────────

	[Theory]
	[InlineData("_Layout.cshtml")]
	[InlineData("_AdminLayout.cshtml")]
	[InlineData("_ShareLayout.cshtml")]
	public void EveryLayout_WrapsItsMasthead_InABannerLandmark(string layout)
	{
		// The GOV.UK Frontend header component renders a plain <div>, so without this wrapper
		// the GOV.UK home link (and, on _Layout, the phase banner's feedback link) sit in no
		// landmark at all and are unreachable by landmark navigation.
		var view = ReadView("Views", "Shared", layout);

		Assert.Contains("<header role=\"banner\"", view);
		Assert.Contains("</header>", view);
		// The wrapper has to enclose the header component, not sit beside it.
		Assert.True(
			view.IndexOf("<header role=\"banner\"", StringComparison.Ordinal)
				< view.IndexOf("<govuk-header", StringComparison.Ordinal),
			$"{layout}: <header role=\"banner\"> must open before <govuk-header>.");
	}

	[Fact]
	public void MainLayout_PutsThePhaseBannerFeedbackLink_InsideTheBannerLandmark()
	{
		var view = ReadView("Views", "Shared", "_Layout.cshtml");

		var bannerOpen = view.IndexOf("<header role=\"banner\">", StringComparison.Ordinal);
		var bannerClose = view.IndexOf("</header>", StringComparison.Ordinal);
		var feedbackLink = view.IndexOf("href=\"/feedback-link\"", StringComparison.Ordinal);

		Assert.True(bannerOpen >= 0 && bannerClose > bannerOpen, "Layout must open and close a banner landmark.");
		Assert.InRange(feedbackLink, bannerOpen, bannerClose);
	}

	// ── #377 "Skip to main content" lands on the breadcrumb ───────────────────────────

	[Fact]
	public void ContentPage_RendersItsBreadcrumb_InTheLayoutSection_NotInline()
	{
		// Inside <main> the breadcrumb is the first thing the skip link reaches, which hands
		// the user straight back into repeated navigation. The layout renders the section
		// before <main>.
		var content = ReadView("Views", "Page", "Content.cshtml");
		var layout = ReadView("Views", "Shared", "_Layout.cshtml");

		Assert.Contains("@section Breadcrumbs {", content);
		Assert.Contains("RenderSection(\"Breadcrumbs\", required: false)", layout);

		// The section must be rendered outside <main>, which for this layout means inside the
		// Header section — after the banner landmark closes.
		var headerSectionStart = layout.IndexOf("@section Header {", StringComparison.Ordinal);
		var breadcrumbRender = layout.IndexOf("RenderSection(\"Breadcrumbs\"", StringComparison.Ordinal);
		var renderBody = layout.IndexOf("@RenderBody()", StringComparison.Ordinal);
		Assert.InRange(breadcrumbRender, headerSectionStart, renderBody);
	}

	[Fact]
	public void ContentPageSpacingRules_DoNotDependOnTheBreadcrumbSittingInsideMain()
	{
		// The old selectors keyed off the breadcrumb being an ancestor (main:has(...)) or the
		// grid row's previous sibling (.cpb-breadcrumbs + .govuk-grid-row). Both stopped
		// matching when the breadcrumb moved out of <main>, silently changing every CMS
		// page's spacing. The replacements key off body:has(...) and the .cpb-content wrapper.
		var css = ReadCss();
		var content = ReadView("Views", "Page", "Content.cshtml");

		Assert.DoesNotContain("main.govuk-main-wrapper:has(.cpb-breadcrumbs)", css);
		Assert.DoesNotContain(".cpb-breadcrumbs + .govuk-grid-row", css);
		Assert.Contains("body:has(.cpb-breadcrumbs) main.govuk-main-wrapper", css);
		Assert.Contains(".cpb-content > .govuk-grid-row:first-child", css);
		Assert.Contains("class=\"cpb-content\"", content);
	}

	// ── #378 / #385 "View" and "Delete" links are indistinguishable ───────────────────

	[Fact]
	public void AmendmentRequests_RowActionLinks_CarryHiddenPupilAndReferenceContext()
	{
		// Every row repeats the same words, so on their own these links are identical both to
		// anyone tabbing and to anyone listing the page's links.
		var view = ReadView("Views", "AmendmentRequests", "Index.cshtml");

		foreach (var label in new[] { ">Edit</a>", ">View</a>", ">Delete</a>" })
		{
			Assert.DoesNotContain(label, view);
		}

		// One suffix per action link: Edit + Delete on the drafts table, View + Delete on the
		// submitted table.
		var occurrences = view.Split("<span class=\"govuk-visually-hidden\"> request @row.ReferenceNumber for @row.PupilName</span>").Length - 1;
		Assert.Equal(4, occurrences);
	}

	// ── #379 Repeated "Continue" buttons on the landing page ──────────────────────────

	[Fact]
	public void LandingPage_ContinueButton_NamesItsCheckingWindow()
	{
		var view = ReadView("Views", "LandingPage", "Index.cshtml");

		Assert.DoesNotContain(">\n                            Continue\n                        </a>", view.Replace("\r\n", "\n"));
		Assert.Contains("Continue<span class=\"govuk-visually-hidden\"> to @openWindow.Title</span>", view);
	}

	// ── #386 Decorative separators announced by VoiceOver ─────────────────────────────

	[Theory]
	[InlineData("Views/LandingPage/Index.cshtml")]
	[InlineData("Views/AmendmentRequests/Index.cshtml")]
	[InlineData("Views/Journey/_FileUpload.cshtml")]
	[InlineData("Views/Shared/ContentPages/Widgets/_Divider.cshtml")]
	public void DecorativeSectionBreaks_AreHiddenFromAssistiveTechnology(string relativePath)
	{
		// A visible <hr> maps to the separator role and is announced, even though these rules
		// are pure decoration — the headings around them already carry the structure.
		var view = ReadView(relativePath.Split('/'));

		foreach (var rule in FindVisibleSectionBreaks(view))
		{
			Assert.Contains("aria-hidden=\"true\"", rule);
		}
	}

	private static IEnumerable<string> FindVisibleSectionBreaks(string view)
	{
		var index = 0;
		while (true)
		{
			var start = view.IndexOf("<hr class=\"govuk-section-break", index, StringComparison.Ordinal);
			if (start < 0) yield break;
			var end = view.IndexOf('>', start);
			var rule = view[start..(end + 1)];
			index = end + 1;
			if (rule.Contains("govuk-section-break--visible", StringComparison.Ordinal)) yield return rule;
		}
	}

	// ── #383 Radios not announced by TalkBack ─────────────────────────────────────────

	[Theory]
	[InlineData("_Radio.cshtml")]
	[InlineData("_Checkbox.cshtml")]
	public void OptionGroups_DescribeTheirGroupHintAndError_OnTheFieldset(string partial)
	{
		// A hint or error rendered next to the group is announced only if the fieldset points
		// at it. The id list is built conditionally because a dangling aria-describedby
		// resolves to nothing and drops the whole description.
		var view = ReadView("Views", "Journey", partial);

		Assert.Contains("aria-describedby=\"@(describedBy.Length > 0 ? describedBy : null)\"", view);
		Assert.Contains("Model.Question.Hint is not null ? $\"{Model.FieldName}-hint\" : null", view);
		Assert.Contains("Model.HasError ? $\"{Model.FieldName}-error\" : null", view);
	}

	[Theory]
	[InlineData("_Radio.cshtml")]
	[InlineData("_Checkbox.cshtml")]
	public void OptionGroups_DescribeAnOptionsOwnHint_OnThatOptionsInput(string partial)
	{
		// Each option's SubLabel is rendered with an id that nothing referenced, so a per-option
		// hint was never announced. Built on the same condition as the element it names.
		var view = ReadView("Views", "Journey", partial);

		Assert.Contains("aria-describedby=\"@(option.SubLabel is not null ? optionId + \"-item-hint\" : null)\"", view);
		Assert.Contains("id=\"@(optionId + \"-item-hint\")\"", view.Replace("@(optionId+\"-item-hint\")", "@(optionId + \"-item-hint\")"));
	}

	[Fact]
	public void RadioGroup_CarriesTheGovUkFrontendModule()
	{
		// _Checkbox carries the equivalent; without it the group is plain markup with none of
		// the component's own behaviour bound.
		var view = ReadView("Views", "Journey", "_Radio.cshtml");
		Assert.Contains("data-module=\"govuk-radios\"", view);
	}

	// ── #408 / AB#301933 Enhanced pickers opened with their placeholder text as their value ──

	[Theory]
	[InlineData("_GradeSelect.cshtml")]
	[InlineData("_SyllabusSelect.cshtml")]
	public void EnhancedSelectPickers_StartEmpty_NotWithThePlaceholderOptionAsTheirValue(string partial)
	{
		// enhanceSelectElement copies the *selected* option's text into the new input when no
		// defaultValue is given. Nothing is preselected, so the selected option is the placeholder
		// ("Select revised grade" / "Select"): the input opened already "filled in" — a screen
		// reader announced a value, and showAllValues filtered the scale against that text and
		// found nothing until the user cleared it. Passing an empty defaultValue leaves the field
		// empty; the library still overrides it with a genuinely selected option's text, so a
		// redisplayed or revisited answer is restored without any code here.
		var view = ReadView("Views", "Journey", partial);

		var enhanceAt = view.IndexOf("accessibleAutocomplete.enhanceSelectElement({", StringComparison.Ordinal);
		Assert.True(enhanceAt >= 0, $"{partial}: the picker must still be enhanced in place with enhanceSelectElement.");
		var configBlock = view.Substring(enhanceAt, view.IndexOf("});", enhanceAt, StringComparison.Ordinal) - enhanceAt);

		Assert.Contains("defaultValue: '',", configBlock);
	}

	[Theory]
	[InlineData("_GradeSelect.cshtml")]
	[InlineData("_SyllabusSelect.cshtml")]
	public void EnhancedSelectPickers_KeepTheNullOptionForTheNoScriptPage_AndOutOfTheSuggestions(string partial)
	{
		// The fix is a script-side default, not a markup change: the placeholder <option value="">
		// must stay so the JavaScript-off <select> has an unselected state, and preserveNullOptions
		// must stay false so that row is never offered as a suggestion. A well-meant "tidy" that
		// drops either would reintroduce a defect on one side or the other.
		var view = ReadView("Views", "Journey", partial);

		Assert.Contains("<option value=\"\">@(Model.Question.SelectPlaceholder ?? \"", view);

		// Scoped to the enhancement call, not the file: the rule is "we never pass the component's
		// placeholder option", and a comment explaining why we don't must not trip it.
		var enhanceAt = view.IndexOf("accessibleAutocomplete.enhanceSelectElement({", StringComparison.Ordinal);
		Assert.True(enhanceAt >= 0, $"{partial}: the picker must still be enhanced in place with enhanceSelectElement.");
		var configBlock = view.Substring(enhanceAt, view.IndexOf("});", enhanceAt, StringComparison.Ordinal) - enhanceAt);

		Assert.Contains("preserveNullOptions: false,", configBlock);
		// No native placeholder attribute either — GOV.UK guidance, and the very thing this bug is.
		Assert.DoesNotContain("placeholder:", configBlock);
	}
}
