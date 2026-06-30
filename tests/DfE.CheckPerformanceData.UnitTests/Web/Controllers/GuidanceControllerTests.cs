using System.Reflection;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.Guidance;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public sealed class GuidanceControllerTests
{
    // The literal-route actions under test don't touch the content-page service.
    private readonly GuidanceController _sut = new(Substitute.For<IContentPageService>());

    // --- Routing ---

    [Fact]
    public void Index_IsMappedTo_Guidance()
    {
        var template = RouteTemplateOf(nameof(GuidanceController.Index));
        Assert.Equal("/guidance", template);
    }

    [Fact]
    public void Ks4June2026_IsMappedTo_ExpectedSlug()
    {
        var template = RouteTemplateOf(nameof(GuidanceController.Ks4June2026));
        Assert.Equal("/guidance/2026-ks4-june-checking-exercise", template);
    }

    // --- Index (parent stub) ---

    [Fact]
    public void Index_ReturnsView()
    {
        Assert.IsType<ViewResult>(_sut.Index());
    }

    // --- KS4 June 2026 page ---

    [Fact]
    public void Ks4June2026_ReturnsView_WithGuidancePageModel()
    {
        var result = Assert.IsType<ViewResult>(_sut.Ks4June2026());
        Assert.IsType<GuidancePage>(result.Model);
    }

    [Fact]
    public void Ks4June2026_Page_HasOrderedSections_WithStableAnchors()
    {
        var page = Assert.IsType<GuidancePage>(Assert.IsType<ViewResult>(_sut.Ks4June2026()).Model);

        // A long navigable guidance page: the side-nav is driven by this section list.
        Assert.True(page.Sections.Count >= 25, $"expected the full section set, got {page.Sections.Count}");

        // Anchors must be unique (in-page links collide otherwise) and url-slug safe.
        var anchors = page.Sections.Select(s => s.Anchor).ToList();
        Assert.Equal(anchors.Count, anchors.Distinct().Count());
        Assert.All(anchors, a => Assert.Matches("^[a-z0-9-]+$", a));

        // Every section maps to a distinctly-keyed content block under the page namespace.
        var keys = page.Sections.Select(s => s.BlockKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, k => Assert.StartsWith("guidance-ks4-2026-", k));
    }

    [Fact]
    public void Ks4June2026_Page_EverySection_HasItsOwnEditableHeadingBlock()
    {
        var page = Assert.IsType<GuidancePage>(Assert.IsType<ViewResult>(_sut.Ks4June2026()).Model);

        // P1: section headings must themselves be editable content blocks (distinct from the body).
        var headingKeys = page.Sections.Select(s => s.HeadingBlockKey).ToList();
        Assert.Equal(headingKeys.Count, headingKeys.Distinct().Count());
        Assert.All(page.Sections, s =>
        {
            Assert.StartsWith("guidance-ks4-2026-", s.HeadingBlockKey);
            Assert.NotEqual(s.BlockKey, s.HeadingBlockKey);
        });
    }

    [Fact]
    public void Ks4June2026_Page_CoversTheKnownLandmarkSections()
    {
        var page = Assert.IsType<GuidancePage>(Assert.IsType<ViewResult>(_sut.Ks4June2026()).Model);
        var anchors = page.Sections.Select(s => s.Anchor).ToHashSet();

        Assert.Contains("key-dates", anchors);
        Assert.Contains("pupil-removal-reason", anchors);
        Assert.Contains("get-support", anchors);
        // The 14 removal categories nest (level 3) under "Pupil removal reason".
        Assert.Contains("child-missing-education", anchors);
        Assert.Contains("merge-pupils", anchors);
        Assert.Contains("permanently-left-england", anchors);
    }

    [Fact]
    public void Ks4June2026_Page_RemovalCategories_AreLevel3_UnderParent()
    {
        var page = Assert.IsType<GuidancePage>(Assert.IsType<ViewResult>(_sut.Ks4June2026()).Model);
        var sections = page.Sections.ToList();

        var parentIndex = sections.FindIndex(s => s.Anchor == "pupil-removal-reason");
        Assert.True(parentIndex >= 0);
        Assert.Equal(2, sections[parentIndex].Level);

        var childIndex = sections.FindIndex(s => s.Anchor == "child-missing-education");
        Assert.Equal(3, sections[childIndex].Level);
        // Children sit after their parent in document order.
        Assert.True(childIndex > parentIndex);
    }

    [Fact]
    public void Ks4June2026_Page_DeclaresNavBlockAndTitle()
    {
        var page = Assert.IsType<GuidancePage>(Assert.IsType<ViewResult>(_sut.Ks4June2026()).Model);

        Assert.Equal("guidance-ks4-2026-nav", page.NavBlockKey);
        Assert.Equal("guidance-ks4-2026-title", page.TitleBlockKey);
        Assert.Equal("guidance-ks4-2026-intro", page.IntroBlockKey);
        Assert.Equal("guidance-ks4-2026-published", page.PublishedBlockKey);
    }

    private static string? RouteTemplateOf(string actionName)
    {
        var method = typeof(GuidanceController).GetMethod(actionName)!;
        var attr = method.GetCustomAttribute<HttpGetAttribute>();
        return attr?.Template;
    }
}
