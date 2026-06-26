namespace DfE.CheckPerformanceData.Web.Models.Guidance;

/// <summary>
/// View model for a long, navigable guidance page rendered from CMS content blocks.
/// The page template owns the structure (ordered sections, stable anchors, headings);
/// each section's body, the lede and the side-navigation are editable content blocks.
/// </summary>
public sealed record GuidancePage
{
    public required string Title { get; init; }

    /// <summary>Plain-text title content block (BlockType "Title").</summary>
    public required string TitleBlockKey { get; init; }

    /// <summary>Lede / introduction content block shown above the first section.</summary>
    public required string IntroBlockKey { get; init; }

    /// <summary>"Published / last reviewed" blue callout block shown under the lede.</summary>
    public required string PublishedBlockKey { get; init; }

    /// <summary>The single editable content block holding the in-page side navigation.</summary>
    public required string NavBlockKey { get; init; }

    public required IReadOnlyList<GuidanceSection> Sections { get; init; }

    /// <summary>
    /// The 2026 KS4 June checking exercise guidance page. The order here is the document
    /// order and the side-navigation order; the 14 removal categories nest (level 3) under
    /// "Pupil removal reason".
    /// </summary>
    public static GuidancePage Ks4June2026 { get; } = Build();

    private static GuidancePage Build()
    {
        const string ns = "guidance-ks4-2026-";

        GuidanceSection S(string anchor, string title, int level = 2) => new()
        {
            Anchor = anchor,
            NavTitle = title,
            Level = level,
            HeadingBlockKey = ns + anchor + "-heading",
            BlockKey = ns + anchor
        };

        return new GuidancePage
        {
            Title = "How to check your performance measures data: KS4 June 2026",
            TitleBlockKey = ns + "title",
            IntroBlockKey = ns + "intro",
            PublishedBlockKey = ns + "published",
            NavBlockKey = ns + "nav",
            Sections =
            [
                S("changes-since-2025", "Changes to the CYPMD service since 2025"),
                S("key-dates", "Key dates"),
                S("about-this-guidance", "About this guidance"),
                S("what-ks4-measures-cover", "What the KS4 performance measures cover in the 2025 to 2026 academic year"),
                S("how-to-check-your-data", "How to check your data"),
                S("pupils-included", "Pupils included in KS4 performance data"),
                S("pupil-removal-reason", "Pupil removal reason"),
                S("dual-registered-or-moved-school", "Dual registered or moved school", 3),
                S("get-support", "Get support"),
            ]
        };
    }
}
