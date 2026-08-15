using DfE.CheckPerformanceData.Application.ContentStaging;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

public sealed class ContentBundleValidatorTests
{
    private static ContentBundle Bundle(
        List<PageNodeBundleItem>? pages = null,
        List<ContentBlockBundleItem>? blocks = null) => new()
    {
        PageNodes = pages ?? [],
        ContentBlocks = blocks ?? []
    };

    // Content and wiki pages get a version, because that is what the CMS produces — creating one
    // always writes an initial version, so a non-folder page with an empty history is not a
    // state the product can reach and the validator now says so. Folders never carry versions.
    private static PageNodeBundleItem Page(
        string segment = "help", string type = "folder", string title = "Help") =>
        new()
        {
            Id = Guid.NewGuid(),
            Segment = segment,
            Title = title,
            PageType = type,
            Versions = type is "content" or "wiki"
                ? [new PageNodeVersionBundleItem { VersionId = 1, Content = "" }]
                : []
        };

    private static ContentBlockBundleItem Block(string type = "Content", string key = "banner") =>
        new()
        {
            Id = Guid.NewGuid(),
            Key = key,
            BlockType = type,
            Value = ""
        };

    [Fact]
    public void EmptyBundle_ProducesNoIssues()
    {
        Assert.Empty(ContentBundleValidator.Validate(Bundle()));
    }

    [Fact]
    public void MinimalValidBundle_ProducesNoIssues()
    {
        var pages = new List<PageNodeBundleItem>
        {
            Page(segment: "help",     type: "folder"),
            Page(segment: "faq",      type: "content"),
            Page(segment: "wiki-doc", type: "wiki"),
        };
        var blocks = new List<ContentBlockBundleItem>
        {
            Block(type: "Content"),
            Block(type: "Title"),
        };

        Assert.Empty(ContentBundleValidator.Validate(Bundle(pages, blocks)));
    }

    // ── PageType ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("folder")]
    [InlineData("content")]
    [InlineData("wiki")]
    public void PageType_ValidValue_Passes(string type)
    {
        var pages = new List<PageNodeBundleItem> { Page(type: type) };
        Assert.Empty(ContentBundleValidator.Validate(Bundle(pages)));
    }

    [Theory]
    [InlineData("Content")]      // wrong casing
    [InlineData("script")]
    [InlineData("")]
    [InlineData("HTML")]
    public void PageType_InvalidValue_FailsWithCode(string type)
    {
        var pages = new List<PageNodeBundleItem> { Page(type: type) };
        var issues = ContentBundleValidator.Validate(Bundle(pages));
        Assert.Contains(issues, i => i.Code == "PAGE_TYPE_UNKNOWN" && i.Severity == ValidationSeverity.Fatal);
    }

    // ── BlockType ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Content")]
    [InlineData("Title")]
    public void BlockType_ValidValue_Passes(string type)
    {
        var blocks = new List<ContentBlockBundleItem> { Block(type: type) };
        Assert.Empty(ContentBundleValidator.Validate(Bundle(blocks: blocks)));
    }

    [Theory]
    [InlineData("content")]      // wrong casing
    [InlineData("Widget")]
    [InlineData("")]
    public void BlockType_InvalidValue_FailsWithCode(string type)
    {
        var blocks = new List<ContentBlockBundleItem> { Block(type: type) };
        var issues = ContentBundleValidator.Validate(Bundle(blocks: blocks));
        Assert.Contains(issues, i => i.Code == "BLOCK_TYPE_UNKNOWN" && i.Severity == ValidationSeverity.Fatal);
    }

    // ── Segment ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("help")]
    [InlineData("wiki-sandbox")]
    [InlineData("get-me-started-2")]
    [InlineData("a1b2")]
    public void Segment_ValidKebabCase_Passes(string segment)
    {
        var pages = new List<PageNodeBundleItem> { Page(segment: segment) };
        Assert.Empty(ContentBundleValidator.Validate(Bundle(pages)));
    }

    [Fact]
    public void Segment_Empty_FailsWithCode()
    {
        var pages = new List<PageNodeBundleItem> { Page(segment: "") };
        var issues = ContentBundleValidator.Validate(Bundle(pages));
        Assert.Contains(issues, i => i.Code == "SEGMENT_EMPTY" && i.Severity == ValidationSeverity.Fatal);
    }

    [Theory]
    [InlineData("Help")]           // uppercase
    [InlineData("help-")]          // trailing hyphen
    [InlineData("-help")]          // leading hyphen
    [InlineData("help--me")]       // double hyphen
    [InlineData("help/me")]        // path separator
    [InlineData("help me")]        // space
    [InlineData("help.md")]        // dot
    [InlineData("../evil")]        // path traversal attempt
    [InlineData("help​")]     // zero-width space
    [InlineData("help‭")]     // RTL override
    public void Segment_InvalidCharacters_FailsWithCode(string segment)
    {
        var pages = new List<PageNodeBundleItem> { Page(segment: segment) };
        var issues = ContentBundleValidator.Validate(Bundle(pages));
        Assert.Contains(issues, i => i.Code == "SEGMENT_INVALID" && i.Severity == ValidationSeverity.Fatal);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("api")]
    [InlineData("dev")]
    [InlineData("account")]
    [InlineData("signin")]
    [InlineData("signout")]
    [InlineData("healthcheck")]
    [InlineData("healthz")]
    public void Segment_Reserved_FailsWithCode(string segment)
    {
        var pages = new List<PageNodeBundleItem> { Page(segment: segment) };
        var issues = ContentBundleValidator.Validate(Bundle(pages));
        Assert.Contains(issues, i => i.Code == "SEGMENT_RESERVED" && i.Severity == ValidationSeverity.Fatal);
    }

    // ── Total-collection caps ─────────────────────────────────────────────────

    [Fact]
    public void PageCount_OverCap_FailsWithCode()
    {
        var pages = Enumerable.Range(0, ContentBundleValidator.MaxPageNodes + 1)
            .Select(_ => Page())
            .ToList();
        var issues = ContentBundleValidator.Validate(Bundle(pages));
        Assert.Contains(issues, i => i.Code == "BUNDLE_TOO_MANY_PAGES" && i.Severity == ValidationSeverity.Fatal);
    }

    [Fact]
    public void BlockCount_OverCap_FailsWithCode()
    {
        var blocks = Enumerable.Range(0, ContentBundleValidator.MaxContentBlocks + 1)
            .Select(_ => Block())
            .ToList();
        var issues = ContentBundleValidator.Validate(Bundle(blocks: blocks));
        Assert.Contains(issues, i => i.Code == "BUNDLE_TOO_MANY_BLOCKS" && i.Severity == ValidationSeverity.Fatal);
    }

    // ── Per-item size caps ────────────────────────────────────────────────────

    [Fact]
    public void WikiPageVersion_OverCap_FailsWithCode()
    {
        var page = Page(type: "wiki");
        page.Versions.Add(new PageNodeVersionBundleItem
        {
            Content = new string('a', ContentBundleValidator.MaxWikiPageVersionBytes + 1),
        });
        var issues = ContentBundleValidator.Validate(Bundle(new List<PageNodeBundleItem> { page }));
        Assert.Contains(issues, i => i.Code == "PAGE_VERSION_TOO_LARGE" && i.Severity == ValidationSeverity.Fatal);
    }

    [Fact]
    public void WikiPageVersion_AtCap_ProducesNoIssue()
    {
        var page = Page(type: "wiki");
        page.Versions.Add(new PageNodeVersionBundleItem
        {
            Content = new string('a', ContentBundleValidator.MaxWikiPageVersionBytes),
        });
        var issues = ContentBundleValidator.Validate(Bundle(new List<PageNodeBundleItem> { page }));
        Assert.DoesNotContain(issues, i => i.Code == "PAGE_VERSION_TOO_LARGE");
    }

    [Fact]
    public void ContentPageVersion_OverCap_FailsWithCode()
    {
        var page = Page(type: "content");
        page.Versions.Add(new PageNodeVersionBundleItem
        {
            Content = new string('a', ContentBundleValidator.MaxContentPageVersionBytes + 1),
        });
        var issues = ContentBundleValidator.Validate(Bundle(new List<PageNodeBundleItem> { page }));
        Assert.Contains(issues, i => i.Code == "PAGE_VERSION_TOO_LARGE" && i.Severity == ValidationSeverity.Fatal);
    }

    // Content pages carry widget-tree JSON with base64-embedded images and are
    // allowed to be well above the wiki cap — this is the "Editing with widgets"
    // scenario at ~1.04 MB that the old shared cap wrongly rejected.
    [Fact]
    public void ContentPageVersion_AboveWikiCap_ButBelowContentCap_ProducesNoIssue()
    {
        var page = Page(type: "content");
        page.Versions.Add(new PageNodeVersionBundleItem
        {
            // Halfway between the two caps — comfortably above the wiki cap, below the content cap.
            Content = new string('a', ContentBundleValidator.MaxWikiPageVersionBytes * 2),
        });
        var issues = ContentBundleValidator.Validate(Bundle(new List<PageNodeBundleItem> { page }));
        Assert.DoesNotContain(issues, i => i.Code == "PAGE_VERSION_TOO_LARGE");
    }

    [Fact]
    public void ContentBlockValue_OverCap_FailsWithCode()
    {
        var block = Block(type: "Content");
        block = block with { Value = new string('a', ContentBundleValidator.MaxContentBlockValueBytes + 1) };
        var issues = ContentBundleValidator.Validate(Bundle(blocks: new List<ContentBlockBundleItem> { block }));
        Assert.Contains(issues, i => i.Code == "BLOCK_VALUE_TOO_LARGE" && i.Severity == ValidationSeverity.Fatal);
    }

    [Fact]
    public void ContentBlockValue_AtCap_ProducesNoIssue()
    {
        var block = Block(type: "Content");
        block = block with { Value = new string('a', ContentBundleValidator.MaxContentBlockValueBytes) };
        var issues = ContentBundleValidator.Validate(Bundle(blocks: new List<ContentBlockBundleItem> { block }));
        Assert.DoesNotContain(issues, i => i.Code == "BLOCK_VALUE_TOO_LARGE");
    }

    [Fact]
    public void TitleBlock_LargeValue_ProducesNoIssue()
    {
        // Title blocks render as plain text — the payload is bounded by UI intent, not by an
        // HTML-sanitisation-cost concern. Only Content-type blocks pass through the render
        // pipeline where a huge string would cost per-render CPU + memory. Keep the size
        // cap scoped to Content blocks.
        var block = Block(type: "Title");
        block = block with { Value = new string('a', ContentBundleValidator.MaxContentBlockValueBytes + 1) };
        var issues = ContentBundleValidator.Validate(Bundle(blocks: new List<ContentBlockBundleItem> { block }));
        Assert.DoesNotContain(issues, i => i.Code == "BLOCK_VALUE_TOO_LARGE");
    }

    // ── Exception plumbing ────────────────────────────────────────────────────

    [Fact]
    public void ContentImportValidationException_CarriesIssues_AndFormatsCount()
    {
        var issues = new List<ValidationIssue>
        {
            new(ValidationSeverity.Fatal, "SEGMENT_INVALID", "seg bad"),
            new(ValidationSeverity.Fatal, "PAGE_TYPE_UNKNOWN", "type bad"),
        };
        var ex = new ContentImportValidationException(issues);

        Assert.Equal(2, ex.Issues.Count);
        Assert.Contains("2 fatal issue(s)", ex.Message);
        Assert.Contains("seg bad", ex.Message);
        Assert.Contains("type bad", ex.Message);
    }
}
