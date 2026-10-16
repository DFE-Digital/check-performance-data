using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Up-front validation for content-staging bundles. Runs before Preview and Import to reject
// bundles that carry malformed identity metadata — the kind of value that would corrupt a URL
// path, sit in the DB as an unknown enum value, or DoS the parser via oversized collections.
//
// Fatal issues are about the bundle as a whole and short-circuit the flow via
// ContentImportValidationException. A Rejected issue is about one item (ItemId): that item is
// left out and listed, and the rest still imports, so one broken page in a whole-site export
// does not stop the others landing. Warning issues are surfaced to the operator but do not
// block anything. HTML sanitisation is a separate
// concern (see ContentBundleSanitiser) — the validator does not inspect Content payloads.
public static class ContentBundleValidator
{
    // Total-collection caps: a legitimate whole-environment export tops out around a few
    // hundred pages + blocks; these are set well above realistic usage so we only reject
    // bundles that are almost certainly a bug or a DoS attempt.
    public const int MaxPageNodes = 5000;
    public const int MaxContentBlocks = 5000;

    // Per-item size caps in characters. The two page-body caps are split by PageType
    // because the cost model is different:
    //
    //   * wiki pages carry raw HTML that passes through Ganss.Xss on import AND on
    //     every render — sanitiser cost scales roughly linearly with input size, so
    //     the cap is tight (1 MB is generous compared to the DfE authoring guidance
    //     target of ~20 KB, but bounded enough that a huge page can't slow reader
    //     traffic).
    //   * content pages carry a widget-tree JSON that is NOT sanitised whole at
    //     import time (individual richtext widget bodies are sanitised at render).
    //     Embedded images as base64 data URIs are a routine authoring pattern (the
    //     "Editing with widgets" help page is ~1.04 MB from two embedded PNGs); an
    //     8 MB cap covers realistic embed-heavy content and still bounds worst-case
    //     storage cost per page well below the 50 MB per-bundle upload cap.
    //
    // Content blocks always carry HTML that renders through the sanitiser (Content
    // block type only — Title is plain text), so their cap tracks the wiki page
    // rationale at a smaller size (blocks are typically banners, footers, short
    // callouts — a couple of paragraphs at most).
    public const int MaxWikiPageVersionBytes = 1_048_576;      // 1 MB per wiki version
    public const int MaxContentPageVersionBytes = 8_388_608;   // 8 MB per content-page version
    public const int MaxContentBlockValueBytes = 262_144;      // 256 KB per Content block

    public const int MaxHomeBanners = 100;
    public const int MaxHomeBannerHeadingLength = 200;
    public const int MaxHomeBannerBodyBytes = 262_144;      // 256 KB, as a Content block

    private static readonly HashSet<string> ValidPageTypes = new(StringComparer.Ordinal)
    {
        "folder", "content", "wiki"
    };

    private static readonly HashSet<string> ValidBlockTypes = new(StringComparer.Ordinal)
    {
        "Content", "Title"
    };

    // Segments that shadow first-party routes or ambiguous URL structure. The framework
    // route table has the final say — this list is defence-in-depth so a bundle can't
    // land content at a path that would confuse admin navigation or clash with
    // authenticated routes.
    private static readonly HashSet<string> ReservedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "api", "dev", "account", "signin", "signout", "healthcheck", "healthz", "_"
    };

    public static IReadOnlyList<ValidationIssue> Validate(ContentBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var issues = new List<ValidationIssue>();

        if (bundle.PageNodes.Count > MaxPageNodes)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Fatal,
                "BUNDLE_TOO_MANY_PAGES",
                $"Bundle has {bundle.PageNodes.Count} pages; the limit is {MaxPageNodes}."));
        }

        if (bundle.ContentBlocks.Count > MaxContentBlocks)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Fatal,
                "BUNDLE_TOO_MANY_BLOCKS",
                $"Bundle has {bundle.ContentBlocks.Count} content blocks; the limit is {MaxContentBlocks}."));
        }

        foreach (var page in bundle.PageNodes)
        {
            ValidatePage(page, issues);
        }

        FindLowercaseClashes(bundle.PageNodes, issues);

        foreach (var block in bundle.ContentBlocks)
        {
            ValidateBlock(block, issues);
        }

        if (bundle.HomeBanners.Count > MaxHomeBanners)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Fatal, "BUNDLE_TOO_MANY_BANNERS",
                $"Bundle has {bundle.HomeBanners.Count} home page banners; the limit is {MaxHomeBanners}."));
        }

        foreach (var banner in bundle.HomeBanners)
        {
            ValidateBanner(banner, issues);
        }

        return issues;
    }

    private static void ValidatePage(PageNodeBundleItem page, List<ValidationIssue> issues)
    {
        var label = Label(page);

        if (!ValidPageTypes.Contains(page.PageType))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Rejected,
                "PAGE_TYPE_UNKNOWN",
                $"Page '{label}' has unknown PageType '{page.PageType}' (expected: folder, content, wiki).",
                page.Id));
        }

        // Capitals are the one mistake the import puts right, because the CMS used to save them.
        var segment = PageSegment.Normalise(page.Segment);
        if (segment.Length == 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Rejected,
                "SEGMENT_EMPTY",
                $"Page '{label}' has an empty Segment.",
                page.Id));
        }
        else if (!PageSegment.IsValid(segment))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Rejected,
                "SEGMENT_INVALID",
                $"Page '{label}' has invalid Segment '{page.Segment}' (must be kebab-case: a-z, 0-9, single hyphens).",
                page.Id));
        }
        else if (ReservedSegments.Contains(segment))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Rejected,
                "SEGMENT_RESERVED",
                $"Page '{label}' uses reserved Segment '{page.Segment}'.",
                page.Id));
        }
        else if (segment != page.Segment)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "SEGMENT_LOWERCASED",
                $"Page '{label}' has URL segment '{page.Segment}', so it is imported as '{segment}'.",
                page.Id));
        }

        // Creating a content or wiki page always writes an initial version, so a non-folder
        // page with no versions is not a state the CMS can produce — it comes from a
        // hand-edited bundle or a faulty export. It imports without complaint and lands a page
        // in the tree that resolves to no live version and permanently 404s, and a re-export
        // carries it onward. Not fatal, because refusing a whole bundle over one such page
        // would be a harsher change than the defect warrants, but the operator has to be told.
        if (page.PageType is "content" or "wiki" && page.Versions.Count == 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "PAGE_HAS_NO_VERSIONS",
                $"Page '{label}' is a {page.PageType} page with no versions — it will not render until content is added.",
                page.Id));
        }

        // Wiki pages carry HTML that hits the sanitiser + render pipeline; content pages
        // carry widget-tree JSON that can legitimately include base64-encoded embedded
        // images. Apply the right cap per PageType and skip the check for folders
        // (which don't carry version bodies anyway).
        var perVersionCap = page.PageType switch
        {
            "wiki"    => MaxWikiPageVersionBytes,
            "content" => MaxContentPageVersionBytes,
            _         => (int?)null, // folder or unknown-type: rely on PAGE_TYPE_UNKNOWN + no body check
        };

        if (perVersionCap is int cap)
        {
            foreach (var version in page.Versions)
            {
                if (version.Content.Length > cap)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Rejected,
                        "PAGE_VERSION_TOO_LARGE",
                        $"Page '{label}' has a version with Content {version.Content.Length} chars; the limit for {page.PageType} pages is {cap}.",
                        page.Id));
                }
            }
        }
    }

    private static void ValidateBlock(ContentBlockBundleItem block, List<ValidationIssue> issues)
    {
        if (!ValidBlockTypes.Contains(block.BlockType))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Rejected,
                "BLOCK_TYPE_UNKNOWN",
                $"Content block '{block.Key}' has unknown BlockType '{block.BlockType}' (expected: Content, Title).",
                block.Id));
        }

        // Only Content-type blocks pass through the HTML sanitiser + render pipeline;
        // Title blocks render as plain text and don't share the per-render cost.
        if (block.BlockType == "Content" && block.Value.Length > MaxContentBlockValueBytes)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Rejected,
                "BLOCK_VALUE_TOO_LARGE",
                $"Content block '{block.Key}' has Value {block.Value.Length} chars; the limit is {MaxContentBlockValueBytes}.",
                block.Id));
        }
    }

    private static void ValidateBanner(HomeBannerBundleItem banner, List<ValidationIssue> issues)
    {
        var label = string.IsNullOrEmpty(banner.Heading) ? banner.Id.ToString() : banner.Heading;

        if (string.IsNullOrWhiteSpace(banner.Heading))
            issues.Add(Rejected(banner, "BANNER_HEADING_EMPTY", $"Home page banner {banner.Id} has an empty heading."));
        else if (banner.Heading.Length > MaxHomeBannerHeadingLength)
            issues.Add(Rejected(banner, "BANNER_HEADING_TOO_LONG", $"Home page banner '{label}' has a heading of {banner.Heading.Length} characters; the limit is {MaxHomeBannerHeadingLength}."));

        if (banner.Body.Length > MaxHomeBannerBodyBytes)
            issues.Add(Rejected(banner, "BANNER_BODY_TOO_LARGE", $"Home page banner '{label}' has a body of {banner.Body.Length} chars; the limit is {MaxHomeBannerBodyBytes}."));

        if (banner.ShowFrom is { } from && banner.ShowUntil is { } until && until <= from)
            issues.Add(Rejected(banner, "BANNER_DATES_INVALID", $"Home page banner '{label}' has a Show until that is not after its Show from."));

        // Versions are replayed into the database too, so a bad version must keep the banner out
        // here rather than fail at SaveChanges, where it would leave the shared DbContext dirty.
        foreach (var v in banner.Versions)
        {
            if (string.IsNullOrWhiteSpace(v.Heading))
                issues.Add(Rejected(banner, "BANNER_HEADING_EMPTY", $"Home page banner '{label}' version {v.VersionNumber} has an empty heading."));
            else if (v.Heading.Length > MaxHomeBannerHeadingLength)
                issues.Add(Rejected(banner, "BANNER_HEADING_TOO_LONG", $"Home page banner '{label}' version {v.VersionNumber} has a heading of {v.Heading.Length} characters; the limit is {MaxHomeBannerHeadingLength}."));

            if (v.ShowFrom is { } vFrom && v.ShowUntil is { } vUntil && vUntil <= vFrom)
                issues.Add(Rejected(banner, "BANNER_DATES_INVALID", $"Home page banner '{label}' version {v.VersionNumber} has a Show until that is not after its Show from."));

            if (v.Body.Length > MaxHomeBannerBodyBytes)
                issues.Add(Rejected(banner, "BANNER_BODY_TOO_LARGE", $"Home page banner '{label}' version {v.VersionNumber} has a body of {v.Body.Length} chars; the limit is {MaxHomeBannerBodyBytes}."));
        }
    }

    private static ValidationIssue Rejected(HomeBannerBundleItem banner, string code, string message) =>
        new(ValidationSeverity.Rejected, code, message, banner.Id);

    // Two pages under one parent cannot share a segment, and lowercasing a segment with capitals
    // can make it the same as a sibling's. The lowercased page is left out rather than merged
    // into the other one.
    private static void FindLowercaseClashes(IReadOnlyList<PageNodeBundleItem> pages, List<ValidationIssue> issues)
    {
        var siblingGroups = pages
            .Where(p => PageSegment.IsValid(PageSegment.Normalise(p.Segment)))
            .GroupBy(p => (p.ParentId, Segment: PageSegment.Normalise(p.Segment)))
            .Where(g => g.Count() > 1);

        foreach (var siblings in siblingGroups)
        {
            // The page already in lowercase keeps the segment; failing that, the first one does.
            var keeper = siblings.FirstOrDefault(p => p.Segment == siblings.Key.Segment) ?? siblings.First();
            foreach (var page in siblings.Where(p => p != keeper && p.Segment != siblings.Key.Segment))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Rejected,
                    "SEGMENT_CLASH",
                    $"Page '{Label(page)}' has URL segment '{page.Segment}'. In lowercase that is the same as page '{Label(keeper)}' beside it, so it is not imported.",
                    page.Id));
            }
        }
    }

    private static string Label(PageNodeBundleItem page) =>
        string.IsNullOrEmpty(page.Title) ? page.Id.ToString() : page.Title;
}

public enum ValidationSeverity
{
    Warning,
    /// <summary>The item named by <see cref="ValidationIssue.ItemId"/> is left out of the import.</summary>
    Rejected,
    /// <summary>The whole bundle is refused.</summary>
    Fatal
}

/// <param name="ItemId">The page, block or banner the issue is about, when it is about one.</param>
public sealed record ValidationIssue(ValidationSeverity Severity, string Code, string Message, Guid? ItemId = null);

public sealed class ContentImportValidationException(IReadOnlyList<ValidationIssue> issues)
    : Exception(BuildMessage(issues))
{
    public IReadOnlyList<ValidationIssue> Issues { get; } = issues;

    private static string BuildMessage(IReadOnlyList<ValidationIssue> issues)
    {
        var fatalCount = issues.Count(i => i.Severity == ValidationSeverity.Fatal);
        return $"Bundle failed validation: {fatalCount} fatal issue(s). {string.Join(" | ", issues.Select(i => i.Message))}";
    }
}
