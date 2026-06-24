namespace DfE.CheckPerformanceData.Web.Models.Guidance;

/// <summary>
/// One navigable section of a guidance page. The manifest is the single source of
/// truth: it drives the in-page side navigation, the rendered heading, the stable
/// anchor the side-nav links to, and the key of the editable content block that
/// holds the section's body prose.
/// </summary>
public sealed record GuidanceSection
{
    /// <summary>Stable, url-slug-safe id used as the section's anchor target (template-owned, never sanitised).</summary>
    public required string Anchor { get; init; }

    /// <summary>Heading text, also used as the side-nav link label.</summary>
    public required string NavTitle { get; init; }

    /// <summary>Heading level: 2 for top-level sections, 3 for nested (e.g. removal categories).</summary>
    public int Level { get; init; } = 2;

    /// <summary>Key of the editable Title block holding this section's heading text.</summary>
    public required string HeadingBlockKey { get; init; }

    /// <summary>Key of the editable content block holding this section's body.</summary>
    public required string BlockKey { get; init; }

    /// <summary>GDS heading class for this section's level (govuk-heading-l for L2, -m for L3).</summary>
    public string HeadingCssClass => Level == 3 ? "govuk-heading-m" : "govuk-heading-l";

    /// <summary>HTML heading element name for this section's level.</summary>
    public string HeadingElement => Level == 3 ? "h3" : "h2";
}
