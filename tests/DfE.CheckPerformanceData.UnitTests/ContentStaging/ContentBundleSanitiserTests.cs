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
}
