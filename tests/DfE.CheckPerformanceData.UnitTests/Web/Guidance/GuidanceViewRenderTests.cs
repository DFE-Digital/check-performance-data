namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Guidance;

// Static Razor-source assertions for the guidance page template. They verify the
// template wires the section manifest (anchors, headings, per-section content blocks)
// and the editable nav block correctly. Rendered-HTML anchor behaviour is verified
// live (Playwright) once the page is seeded.
public sealed class GuidanceViewRenderTests
{
    private static string RepoRoot()
    {
        var thisFile = ThisFilePath();
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", ".."));
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    private static string ReadGuidanceView(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", "Guidance", name));

    private static string Ks4View() => ReadGuidanceView("Ks4June2026.cshtml");
    private static string IndexView() => ReadGuidanceView("Index.cshtml");

    // --- KS4 page template ---

    [Fact]
    public void Ks4_View_Exists_And_BindsGuidancePageModel()
    {
        var src = Ks4View();
        Assert.Contains("GuidancePage", src);
        Assert.Contains("@model", src);
    }

    [Fact]
    public void Ks4_View_HasBreadcrumbs_HomeGuidanceCurrent()
    {
        var src = Ks4View();
        Assert.Contains("govuk-breadcrumbs", src);
        Assert.Contains("href=\"/\"", src);
        Assert.Contains("href=\"/guidance\"", src);
    }

    [Fact]
    public void Ks4_View_RendersTitleIntroAndNavBlocks()
    {
        var src = Ks4View();
        // Editable title (plain) + editable intro + the single editable side-nav block.
        Assert.Contains("EditableTitle", src);
        Assert.Contains("Model.TitleBlockKey", src);
        Assert.Contains("EditableContent", src);
        Assert.Contains("Model.IntroBlockKey", src);
        Assert.Contains("Model.NavBlockKey", src);
        // The blue "Published / last reviewed" callout (missing in the first pass).
        Assert.Contains("Model.PublishedBlockKey", src);
    }

    [Fact]
    public void Ks4_View_SectionHeadings_AreEditableTitleBlocks()
    {
        var src = Ks4View();
        // P1: headings must be editable content blocks, not static template text.
        Assert.Contains("EditableTitle", src);
        Assert.Contains(".HeadingBlockKey", src);
        // No hard-coded <h2>/<h3> heading text for sections any more.
        Assert.DoesNotContain("<h2 class=\"govuk-heading-l\">@sec.NavTitle</h2>", src);
    }

    [Fact]
    public void Ks4_View_TwoColumnLayout_NavThenContent()
    {
        var src = Ks4View();
        Assert.Contains("govuk-grid-column-one-third", src);
        Assert.Contains("govuk-grid-column-two-thirds", src);
    }

    [Fact]
    public void Ks4_View_IteratesSections_WithStableAnchorsAndPerSectionBlocks()
    {
        var src = Ks4View();
        // Loops the manifest, emits a template-owned anchor per section, the heading from
        // the manifest, and the section body as an EditableContent block keyed by the manifest.
        Assert.Matches(@"foreach\s*\(\s*var\s+\w+\s+in\s+Model\.Sections", src);
        Assert.Contains(".Anchor", src);
        Assert.Contains(".NavTitle", src);
        Assert.Contains(".BlockKey", src);
        // Anchor target lives on a template element id, not inside a (sanitised) content block.
        Assert.Matches(@"id=""@\w+\.Anchor""", src);
    }

    [Fact]
    public void Ks4_View_RendersHeadingLevelFromManifest()
    {
        var src = Ks4View();
        // Manifest drives h2 vs h3 (and the matching GDS class) so nested removal
        // categories render as sub-headings.
        Assert.Contains(".HeadingElement", src);
        Assert.Contains(".HeadingCssClass", src);
    }

    [Fact]
    public void Ks4_View_HasBackToTopLink()
    {
        var src = Ks4View();
        Assert.Contains("Back to top", src);
    }

    // --- Guidance landing stub ---

    [Fact]
    public void Index_View_Exists_WithBreadcrumbAndCaption()
    {
        var src = IndexView();
        Assert.Contains("govuk-breadcrumbs", src);
        Assert.Contains("CYPMD help and support", src);
        Assert.Contains("govuk-caption-l", src);
    }

    [Fact]
    public void Index_View_AssemblesEditableBlocks_IncludingHeadings()
    {
        var src = IndexView();
        // Every landing item is an editable block (P1), headings included.
        Assert.Contains("EditableTitle", src);
        Assert.Contains("EditableContent", src);
        Assert.Contains("guidance-landing-title", src);
        Assert.Contains("guidance-landing-signin-heading", src);
        // The three data-check sections (cards live in their own editable blocks).
        Assert.Contains("-cards", src);
    }
}
