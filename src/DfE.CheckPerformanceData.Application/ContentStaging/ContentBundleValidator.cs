using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Up-front validation for content-staging bundles. Runs before Preview and Import to reject
// bundles that carry malformed identity metadata — the kind of value that would corrupt a URL
// path, sit in the DB as an unknown enum value, or DoS the parser via oversized collections.
//
// Fatal issues short-circuit the flow via ContentImportValidationException; warning issues are
// surfaced to the operator but do not block the import. HTML sanitisation is a separate
// concern (see ContentBundleSanitiser) — the validator does not inspect Content payloads.
public static partial class ContentBundleValidator
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

        foreach (var block in bundle.ContentBlocks)
        {
            ValidateBlock(block, issues);
        }

        return issues;
    }

    private static void ValidatePage(PageNodeBundleItem page, List<ValidationIssue> issues)
    {
        var label = string.IsNullOrEmpty(page.Title) ? page.Id.ToString() : page.Title;

        if (!ValidPageTypes.Contains(page.PageType))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Fatal,
                "PAGE_TYPE_UNKNOWN",
                $"Page '{label}' has unknown PageType '{page.PageType}' (expected: folder, content, wiki)."));
        }

        if (string.IsNullOrEmpty(page.Segment))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Fatal,
                "SEGMENT_EMPTY",
                $"Page '{label}' has an empty Segment."));
        }
        else if (!SegmentPattern().IsMatch(page.Segment))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Fatal,
                "SEGMENT_INVALID",
                $"Page '{label}' has invalid Segment '{page.Segment}' (must be kebab-case: a-z, 0-9, single hyphens)."));
        }
        else if (ReservedSegments.Contains(page.Segment))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Fatal,
                "SEGMENT_RESERVED",
                $"Page '{label}' uses reserved Segment '{page.Segment}'."));
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
                        ValidationSeverity.Fatal,
                        "PAGE_VERSION_TOO_LARGE",
                        $"Page '{label}' has a version with Content {version.Content.Length} chars; the limit for {page.PageType} pages is {cap}."));
                }
            }
        }
    }

    private static void ValidateBlock(ContentBlockBundleItem block, List<ValidationIssue> issues)
    {
        if (!ValidBlockTypes.Contains(block.BlockType))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Fatal,
                "BLOCK_TYPE_UNKNOWN",
                $"Content block '{block.Key}' has unknown BlockType '{block.BlockType}' (expected: Content, Title)."));
        }

        // Only Content-type blocks pass through the HTML sanitiser + render pipeline;
        // Title blocks render as plain text and don't share the per-render cost.
        if (block.BlockType == "Content" && block.Value.Length > MaxContentBlockValueBytes)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Fatal,
                "BLOCK_VALUE_TOO_LARGE",
                $"Content block '{block.Key}' has Value {block.Value.Length} chars; the limit is {MaxContentBlockValueBytes}."));
        }
    }

    // URL-safe kebab-case: lowercase alphanumerics separated by single hyphens.
    // Deliberately does not allow underscores, uppercase, or trailing/leading hyphens.
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled)]
    private static partial Regex SegmentPattern();
}

public enum ValidationSeverity
{
    Warning,
    Fatal
}

public sealed record ValidationIssue(ValidationSeverity Severity, string Code, string Message);

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
