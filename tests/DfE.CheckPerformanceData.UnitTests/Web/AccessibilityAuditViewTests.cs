namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Static Razor-source assertions for the fixes raised by the Zoonou accessibility audit
// (epic #384) and for other view-level accessibility defects fixed since. Same hostless
// pattern as LayoutRenderTests: read the .cshtml as text and assert on the source, so the
// suite needs no MVC test harness.
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

	// ── Close checking exercise: repeated control names ───────────────────────────────

	[Fact]
	public void SummaryPage_CloseButton_NamesItsExerciseInVisibleText()
	{
		// The Close button repeats once per exercise section down the page. Identical accessible
		// names would leave no way to tell one section's button from the next (#378/#385/#379).
		// Here the exercise label is in the VISIBLE text, which satisfies the rule without a
		// govuk-visually-hidden suffix — so the label must not be dropped from the button.
		var view = ReadView("Views", "WindowAdmin", "Summary.cshtml");

		Assert.Contains("Close @exercise.Label", view);
		Assert.Contains("@exercise.CloseLink", view);
	}

	[Fact]
	public void SummaryPage_SendRequestsButton_NamesItsExerciseInVisibleText()
	{
		// AB#301022: like Close, this button repeats once per closed exercise down the page, so
		// the exercise label must stay in its visible text.
		var view = ReadView("Views", "WindowAdmin", "Summary.cshtml");

		Assert.Contains("Send @exercise.Label requests for processing", view);
		Assert.Contains("@exercise.SendRequestsLink", view);
	}

	[Fact]
	public void ClosePage_TitleMatchesItsHeading()
	{
		// Every page sets ViewData/ViewBag Title and it matches the <h1>, same words, sentence case.
		var view = ReadView("Views", "WindowAdmin", "Close.cshtml");

		Assert.Contains("ViewBag.Title = $\"Close {Model.ExerciseLabel} early?\";", view);
		Assert.Contains("<h1 class=\"govuk-heading-l\">Close @Model.ExerciseLabel early?</h1>", view);
	}

	[Fact]
	public void SendRequestsPage_TitleMatchesItsHeading()
	{
		// AB#301022: same rule as the Close page — the title and the <h1> are the same words.
		var view = ReadView("Views", "WindowAdmin", "SendRequests.cshtml");

		Assert.Contains("ViewBag.Title = $\"Send {Model.ExerciseLabel} requests for processing\";", view);
		Assert.Contains("<h1 class=\"govuk-heading-l\">Send @Model.ExerciseLabel requests for processing</h1>", view);
	}

	// ── #389 Redundant hidden instructions on Provide Evidence ────────────────────────

	[Fact]
	public void FileUploadPartial_KeepsTheHintVisible_ButDoesNotNameItInTheInputsDescription()
	{
		// #389: in our reading of the audit (JAWS on Chrome, unconfirmed with the auditor),
		// Chrome exposes a native file input as a button, and JAWS, rather than reading its
		// aria-describedby aloud, tells the user to press JAWS key + Alt + R to hear
		// "descriptive text", which would have been the hint already visible under the heading.
		// The hint stays on the page; the input's description names the count line and,
		// when there is one, the error (QuestionPartialModel.DescribedBy, pinned by
		// QuestionPartialModelDescribedByTests). An id of fileUpload-hint anywhere in the
		// partial would be the first step to bringing the detour back.
		var view = ReadView("Views", "Journey", "_FileUpload.cshtml");

		Assert.Contains("@Model.Question.Hint", view);
		Assert.DoesNotContain("fileUpload-hint", view);
		Assert.Contains("aria-describedby=\"@Model.DescribedBy\"", view);
	}

	// ── #568 Storage browser upload in parts ───────────────────────────────────────────

	[Fact]
	public void StorageUpload_FileInput_IsDescribedByItsHint()
	{
		var view = ReadView("Views", "StorageAdmin", "Container.cshtml");
		Assert.Contains("id=\"files-hint\"", view);
		Assert.Matches("<input[^>]*id=\"files\"[^>]*aria-describedby=\"files-hint\"", view);
	}

	// A live region announces reliably only when it is already in the DOM, empty, before the
	// script writes to it (the same finding as evidence-upload-validation.js).
	[Fact]
	public void StorageUpload_StatusRegion_IsPresentAndEmptyAtLoad()
	{
		var view = ReadView("Views", "StorageAdmin", "Container.cshtml");
		Assert.Contains("<div id=\"upload-status\" class=\"govuk-body\" role=\"status\" aria-live=\"polite\"></div>", view);
	}

	// Cancel is a real button and the no-JS reader is told how to get the bigger limit.
	[Fact]
	public void StorageUpload_HasARealCancelButton_AndANoScriptSentence()
	{
		var view = ReadView("Views", "StorageAdmin", "Container.cshtml");
		Assert.Matches("<button type=\"button\" id=\"upload-cancel\"", view);
		Assert.Contains("To upload bigger files, turn on JavaScript in your browser.", view);
	}

	[Fact]
	public void StorageUpload_ProgressElement_HasAnAccessibleName()
	{
		var view = ReadView("Views", "StorageAdmin", "Container.cshtml");
		Assert.Matches("<progress[^>]*id=\"upload-progress\"[^>]*aria-label=\"Upload progress\"", view);
	}

	// The native progress element is announced by the status line at 25% steps, so the visible
	// percentage is hidden from assistive technology to avoid a second, noisy announcement.
	[Fact]
	public void StorageUpload_VisiblePercentage_IsHiddenFromAssistiveTechnology()
	{
		var view = ReadView("Views", "StorageAdmin", "Container.cshtml");
		Assert.Matches("<span id=\"upload-percent\"[^>]*aria-hidden=\"true\"", view);
	}

	// Author display rules beat the browser's [hidden] rule, so idle upload controls need their own.
	[Fact]
	public void StorageUpload_HiddenControls_AreHiddenByAScopedRule()
	{
		var css = ReadCss();
		Assert.Contains("#storage-upload-form [hidden]", css);
	}

	// ── #566 Start-page banners: one region per banner, each with its own accessible name ──

	[Fact]
	public void HomeBanner_IsARegion_LabelledByItsOwnTitleAndHeading()
	{
		var partial = ReadView("Views", "Shared", "Components", "HomeBanners", "_Banner.cshtml");

		Assert.Contains("role=\"region\"", partial);
		Assert.Contains("data-module=\"govuk-notification-banner\"", partial);
		// Both ids carry the banner's own id, so several banners on one page never share a label.
		Assert.Contains("aria-labelledby=\"home-banner-@Model.Id-title home-banner-@Model.Id-heading\"", partial);
		Assert.Contains("<h2 class=\"govuk-notification-banner__title\" id=\"home-banner-@Model.Id-title\">Important</h2>", partial);
		Assert.Contains("id=\"home-banner-@Model.Id-heading\"", partial);
	}

	[Fact]
	public void StartPage_RendersBannersThroughTheComponent_NotTheOldBetaPartial()
	{
		var index = ReadView("Views", "Home", "Index.cshtml");

		Assert.Contains("Component.InvokeAsync(\"HomeBanners\")", index);
		Assert.DoesNotContain("_BetaBanner", index);
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
		Assert.False(File.Exists(Path.Combine(repoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", "_BetaBanner.cshtml")));
	}
}
