using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentStaging;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

public sealed class ContentBundleSanitiserTests
{
    // Real sanitiser rather than a substitute — the whole point of this class is to
    // exercise the actual Ganss pipeline as the DB will see it.
    private readonly ContentBundleSanitiser _sut = new(new HtmlRenderingService());

    private static PageNodeBundleItem WikiPage(params string[] versionContents)
    {
        var versions = versionContents.Select((c, i) => new PageNodeVersionBundleItem
        {
            VersionId = i + 1,
            Content = c
        }).ToList();
        return new PageNodeBundleItem
        {
            Id = Guid.NewGuid(),
            Segment = "wiki-doc",
            Title = "Wiki doc",
            PageType = "wiki",
            Versions = versions
        };
    }

    private static ContentBlockBundleItem ContentBlock(string value) => new()
    {
        Id = Guid.NewGuid(),
        Key = "banner",
        BlockType = "Content",
        Value = value
    };

    [Fact]
    public void CleanBundle_NothingChanges()
    {
        var bundle = new ContentBundle
        {
            PageNodes = { WikiPage("<p>Hello</p>") },
            ContentBlocks = { ContentBlock("<p>Hi</p>") }
        };

        var changed = _sut.SanitiseInPlace(bundle);

        Assert.Equal(0, changed);
        Assert.Equal("<p>Hello</p>", bundle.PageNodes[0].Versions[0].Content);
        Assert.Equal("<p>Hi</p>", bundle.ContentBlocks[0].Value);
    }

    [Fact]
    public void WikiPage_WithInlineScript_StripsScriptAndIncrementsCounter()
    {
        var bundle = new ContentBundle
        {
            PageNodes = { WikiPage("<p>Hi<script>alert(1)</script></p>") }
        };

        var changed = _sut.SanitiseInPlace(bundle);

        Assert.Equal(1, changed);
        var cleaned = bundle.PageNodes[0].Versions[0].Content;
        Assert.DoesNotContain("<script", cleaned);
        Assert.DoesNotContain("alert(1)", cleaned);
        // The safe body around the script survives.
        Assert.Contains("Hi", cleaned);
    }

    [Fact]
    public void ContentBlock_WithInlineScript_StripsScriptAndIncrementsCounter()
    {
        var bundle = new ContentBundle
        {
            ContentBlocks = { ContentBlock("<p>Buy <script>steal()</script>now</p>") }
        };

        var changed = _sut.SanitiseInPlace(bundle);

        Assert.Equal(1, changed);
        var cleaned = bundle.ContentBlocks[0].Value;
        Assert.DoesNotContain("<script", cleaned);
        Assert.DoesNotContain("steal()", cleaned);
        Assert.Contains("Buy", cleaned);
        Assert.Contains("now", cleaned);
    }

    [Fact]
    public void WikiPage_WithJavascriptUrl_StripsHref()
    {
        var bundle = new ContentBundle
        {
            PageNodes = { WikiPage("<a href=\"javascript:evil()\">click me</a>") }
        };

        _sut.SanitiseInPlace(bundle);

        var cleaned = bundle.PageNodes[0].Versions[0].Content;
        Assert.DoesNotContain("javascript:", cleaned);
        Assert.DoesNotContain("evil()", cleaned);
    }

    [Fact]
    public void WikiPage_WithOnClickAttribute_StripsHandler()
    {
        var bundle = new ContentBundle
        {
            PageNodes = { WikiPage("<div onclick=\"alert(1)\">hi</div>") }
        };

        _sut.SanitiseInPlace(bundle);

        var cleaned = bundle.PageNodes[0].Versions[0].Content;
        Assert.DoesNotContain("onclick", cleaned);
        Assert.DoesNotContain("alert(1)", cleaned);
    }

    [Fact]
    public void ContentTypePage_IsSkipped_EvenIfContentContainsScriptString()
    {
        // Content-typed pages carry a widget-tree JSON, not raw HTML — running it through
        // an HTML sanitiser would corrupt the JSON. The individual rich-text widgets are
        // sanitised at render time. Prove the sanitiser leaves content-type versions alone.
        var contentPage = new PageNodeBundleItem
        {
            Id = Guid.NewGuid(),
            Segment = "faq",
            Title = "FAQ",
            PageType = "content",
            Versions =
            {
                new PageNodeVersionBundleItem
                {
                    VersionId = 1,
                    Content = "{\"widgets\":[{\"type\":\"richtext\",\"html\":\"<p>ok</p>\"}]}"
                }
            }
        };
        var bundle = new ContentBundle { PageNodes = { contentPage } };

        var changed = _sut.SanitiseInPlace(bundle);

        Assert.Equal(0, changed);
        Assert.Equal(
            "{\"widgets\":[{\"type\":\"richtext\",\"html\":\"<p>ok</p>\"}]}",
            bundle.PageNodes[0].Versions[0].Content);
    }

    [Fact]
    public void TitleBlock_IsSkipped_EvenIfValueContainsScriptString()
    {
        var bundle = new ContentBundle
        {
            ContentBlocks =
            {
                new ContentBlockBundleItem
                {
                    Id = Guid.NewGuid(),
                    Key = "page-title",
                    BlockType = "Title",
                    Value = "Welcome <script> hi"
                }
            }
        };

        var changed = _sut.SanitiseInPlace(bundle);

        Assert.Equal(0, changed);
        Assert.Equal("Welcome <script> hi", bundle.ContentBlocks[0].Value);
    }

    [Fact]
    public void FolderPage_IsSkipped()
    {
        // Folders have no versions but let's cover the branch — no crash, no writes.
        var bundle = new ContentBundle
        {
            PageNodes =
            {
                new PageNodeBundleItem
                {
                    Id = Guid.NewGuid(),
                    Segment = "help",
                    Title = "Help",
                    PageType = "folder"
                }
            }
        };

        Assert.Equal(0, _sut.SanitiseInPlace(bundle));
    }

    [Fact]
    public void MultiVersion_OnlyDirtyVersionsCounted()
    {
        var bundle = new ContentBundle
        {
            PageNodes = { WikiPage("<p>clean</p>", "<script>x</script>", "<p>also clean</p>") }
        };

        var changed = _sut.SanitiseInPlace(bundle);

        Assert.Equal(1, changed);
        Assert.Equal("<p>clean</p>",       bundle.PageNodes[0].Versions[0].Content);
        Assert.DoesNotContain("<script",   bundle.PageNodes[0].Versions[1].Content);
        Assert.Equal("<p>also clean</p>",  bundle.PageNodes[0].Versions[2].Content);
    }

    [Fact]
    public void Idempotent_SecondRunIsNoop()
    {
        var bundle = new ContentBundle
        {
            PageNodes = { WikiPage("<p>Hi<script>alert(1)</script></p>") },
            ContentBlocks = { ContentBlock("<a href=\"javascript:evil()\">x</a>") }
        };

        var first = _sut.SanitiseInPlace(bundle);
        var second = _sut.SanitiseInPlace(bundle);

        Assert.Equal(2, first);
        Assert.Equal(0, second);
    }

    // ── Sanitiser bypass regression guards ────────────────────────────────────
    //
    // These payloads are the classic XSS-through-a-sanitiser vectors that need to
    // stay dead. Each asserts on the OUTPUT never containing the executable
    // signature (script tag, javascript: URL, event handler), rather than pinning
    // the exact byte shape — a dependency-driven improvement to the sanitiser
    // shouldn't have to update these tests.
    //
    // Uses the real Ganss.Xss HtmlRenderingService (as elsewhere in this suite) so
    // a regression in the pinned package version surfaces here before it ships.

    [Theory]
    // SVG-embedded <script> — SVG is allowed for icons but its script element must not be.
    [InlineData("<svg><script>alert(1)</script></svg>")]
    // Inline event handler on <img> — the classic XSS one-liner.
    [InlineData("<img src=x onerror=\"alert(1)\">")]
    // javascript: URL on an <a> href — must be stripped or scheme-rewritten.
    [InlineData("<a href=\"javascript:alert(1)\">click</a>")]
    // Mixed-case javascript: URL to catch a case-sensitive-check bypass.
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">click</a>")]
    // data: URL for an <iframe> — a way to get JS-executing HTML into the DOM.
    [InlineData("<iframe src=\"data:text/html,<script>alert(1)</script>\"></iframe>")]
    // CSS @import to steal styles / attempt data exfil via unicode-range abuse.
    [InlineData("<style>@import url('https://evil.example/x.css');</style>")]
    // Mutation-XSS shape via <noscript> — parsers with mixed HTML/JS modes get this
    // wrong; a spec-conformant one strips the <noscript> or the embedded handler.
    [InlineData("<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">")]
    // <object> and <embed> — legacy but still a script-run vector.
    [InlineData("<object data=\"data:text/html,<script>alert(1)</script>\"></object>")]
    // <base> tag hijack — changes the base URL for every relative link on the page.
    [InlineData("<base href=\"https://evil.example/\">")]
    // Attribute-namespace bypass: xlink:href on <use> can point to a data: URL.
    [InlineData("<svg><use xlink:href=\"data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>\"/></svg>")]
    public void KnownXssVectors_AreStrippedFromWikiPageContent(string payload)
    {
        var bundle = new ContentBundle
        {
            PageNodes = { WikiPage(payload) }
        };

        _sut.SanitiseInPlace(bundle);

        var clean = bundle.PageNodes[0].Versions[0].Content;
        AssertNoExecutableSignatures(clean, payload);
    }

    [Theory]
    [InlineData("<svg><script>alert(1)</script></svg>")]
    [InlineData("<img src=x onerror=\"alert(1)\">")]
    [InlineData("<a href=\"javascript:alert(1)\">click</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">click</a>")]
    [InlineData("<iframe src=\"data:text/html,<script>alert(1)</script>\"></iframe>")]
    [InlineData("<style>@import url('https://evil.example/x.css');</style>")]
    [InlineData("<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">")]
    [InlineData("<object data=\"data:text/html,<script>alert(1)</script>\"></object>")]
    [InlineData("<base href=\"https://evil.example/\">")]
    public void KnownXssVectors_AreStrippedFromContentBlockValue(string payload)
    {
        var bundle = new ContentBundle
        {
            ContentBlocks = { ContentBlock(payload) }
        };

        _sut.SanitiseInPlace(bundle);

        AssertNoExecutableSignatures(bundle.ContentBlocks[0].Value, payload);
    }

    // Reads the sanitised output for the signatures that would let script actually
    // run in a browser: script tag anywhere, javascript: URL (case-insensitive),
    // an on* event-handler attribute pattern, a data:text/html source that would
    // execute inline HTML, and a <base> tag that would rewrite relative-URL context.
    // No single string check catches everything; the combination is the barrier.
    private static void AssertNoExecutableSignatures(string sanitised, string original)
    {
        var lower = sanitised.ToLowerInvariant();
        Assert.False(lower.Contains("<script"),
            $"sanitised output contains <script> for input: {original}\noutput: {sanitised}");
        Assert.False(lower.Contains("javascript:"),
            $"sanitised output contains javascript: URL for input: {original}\noutput: {sanitised}");
        // on\w+= attribute handler pattern — onerror=, onload=, onclick=, etc.
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(sanitised, @"\son[a-z]+\s*="),
            $"sanitised output contains on*= handler for input: {original}\noutput: {sanitised}");
        Assert.False(lower.Contains("data:text/html"),
            $"sanitised output preserves data:text/html source for input: {original}\noutput: {sanitised}");
        Assert.False(lower.Contains("<base"),
            $"sanitised output contains <base> for input: {original}\noutput: {sanitised}");
    }
}
