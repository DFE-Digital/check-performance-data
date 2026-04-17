using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.Wiki;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Wiki;

public sealed class WikiServiceTests
{
    private readonly IWikiRepository _repository = Substitute.For<IWikiRepository>();
    private readonly IHtmlRenderingService _htmlRenderer = Substitute.For<IHtmlRenderingService>();
    private readonly WikiService _sut;

    public WikiServiceTests()
    {
        _htmlRenderer.RenderHtml(Arg.Any<string?>()).Returns(ci => $"<p>{ci.Arg<string?>()}</p>");
        _sut = new WikiService(_repository, _htmlRenderer);

        _repository.ExecuteInTransactionAsync(Arg.Any<Func<Task>>())
            .Returns(ci => ((Func<Task>)ci[0])());
    }

    // --- GetNavigationTreeAsync ---

    [Fact]
    public async Task GetNavigationTreeAsync_ReturnsEmptyList_WhenNoPages()
    {
        _repository.GetAllOrderedAsync().Returns(new List<WikiPageDto>());

        var result = await _sut.GetNavigationTreeAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetNavigationTreeAsync_BuildsFlatList_WhenAllRootPages()
    {
        var pages = new List<WikiPageDto>
        {
            MakePage(id: 1, title: "Alpha", slug: "alpha", sortOrder: 0),
            MakePage(id: 2, title: "Beta", slug: "beta", sortOrder: 1)
        };
        _repository.GetAllOrderedAsync().Returns(pages);

        var result = await _sut.GetNavigationTreeAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("alpha", result[0].SlugPath);
        Assert.Equal("beta", result[1].SlugPath);
        Assert.Empty(result[0].Children);
    }

    [Fact]
    public async Task GetNavigationTreeAsync_BuildsNestedTree()
    {
        var pages = new List<WikiPageDto>
        {
            MakePage(id: 1, title: "Parent", slug: "parent", sortOrder: 0),
            MakePage(id: 2, title: "Child", slug: "child", parentId: 1, sortOrder: 0),
            MakePage(id: 3, title: "Grandchild", slug: "grandchild", parentId: 2, sortOrder: 0)
        };
        _repository.GetAllOrderedAsync().Returns(pages);

        var result = await _sut.GetNavigationTreeAsync();

        Assert.Single(result);
        Assert.Equal("parent", result[0].SlugPath);
        Assert.Single(result[0].Children);
        Assert.Equal("parent/child", result[0].Children[0].SlugPath);
        Assert.Single(result[0].Children[0].Children);
        Assert.Equal("parent/child/grandchild", result[0].Children[0].Children[0].SlugPath);
    }

    // --- GetPageBySlugPathAsync ---

    [Fact]
    public async Task GetPageBySlugPathAsync_ReturnsNull_WhenEmptyPath()
    {
        var result = await _sut.GetPageBySlugPathAsync("");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPageBySlugPathAsync_ResolvesSingleSegment()
    {
        var page = MakePage(id: 1, title: "About", slug: "about");
        _repository.GetBySlugAndParentAsync("about", null).Returns(page);

        var result = await _sut.GetPageBySlugPathAsync("about");

        Assert.NotNull(result);
        Assert.Equal("about", result.SlugPath);
    }

    [Fact]
    public async Task GetPageBySlugPathAsync_ResolvesMultiSegmentPath()
    {
        var parent = MakePage(id: 1, title: "Docs", slug: "docs");
        var child = MakePage(id: 2, title: "API", slug: "api", parentId: 1);

        _repository.GetBySlugAndParentAsync("docs", null).Returns(parent);
        _repository.GetBySlugAndParentAsync("api", 1).Returns(child);

        var result = await _sut.GetPageBySlugPathAsync("docs/api");

        Assert.NotNull(result);
        Assert.Equal(2, result.Id);
        Assert.Equal("docs/api", result.SlugPath);
    }

    [Fact]
    public async Task GetPageBySlugPathAsync_ReturnsNull_WhenIntermediateSlugNotFound()
    {
        _repository.GetBySlugAndParentAsync("docs", null).ReturnsNull();

        var result = await _sut.GetPageBySlugPathAsync("docs/api");

        Assert.Null(result);
    }

    // --- GetPageByIdAsync ---

    [Fact]
    public async Task GetPageByIdAsync_ReturnsNull_WhenNotFound()
    {
        _repository.GetByIdAsync(99).ReturnsNull();

        var result = await _sut.GetPageByIdAsync(99);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPageByIdAsync_ReturnsEnrichedDto_WithSlugPath()
    {
        var page = MakePage(id: 1, title: "Home", slug: "home");
        _repository.GetByIdAsync(1).Returns(page);

        var result = await _sut.GetPageByIdAsync(1);

        Assert.NotNull(result);
        Assert.Equal("home", result.SlugPath);
        Assert.NotNull(result.ContentHtml);
    }

    [Fact]
    public async Task GetPageByIdAsync_BuildsSlugPath_ThroughParentChain()
    {
        var child = MakePage(id: 2, title: "Child", slug: "child", parentId: 1);
        var parent = MakePage(id: 1, title: "Parent", slug: "parent");

        _repository.GetByIdAsync(2).Returns(child);
        _repository.GetByIdAsync(1).Returns(parent);

        var result = await _sut.GetPageByIdAsync(2);

        Assert.NotNull(result);
        Assert.Equal("parent/child", result.SlugPath);
    }

    // --- CreatePageAsync ---

    [Fact]
    public async Task CreatePageAsync_CreatesPageWithSlugAndVersion()
    {
        var dto = new CreateWikiPageDto { Title = "New Page", Content = "Content here", ParentId = null };
        var created = MakePage(id: 10, title: "New Page", slug: "new-page");

        _repository.SlugExistsAsync("new-page", null).Returns(false);
        _repository.GetMaxSortOrderAsync(null).Returns(2);
        _repository.AddPageAsync(dto, "new-page", 3).Returns(created);
        _repository.GetByIdAsync(10).Returns(created);

        var result = await _sut.CreatePageAsync(dto);

        Assert.Equal(10, result.Id);
        await _repository.Received(1).AddPageAsync(dto, "new-page", 3);
        await _repository.Received(1).AddVersionAsync(10, "New Page", "Content here", 1);
    }

    [Fact]
    public async Task CreatePageAsync_AppendsTicks_WhenSlugExists()
    {
        var dto = new CreateWikiPageDto { Title = "Duplicate", Content = "Body" };

        _repository.SlugExistsAsync("duplicate", null).Returns(true);
        _repository.GetMaxSortOrderAsync(null).Returns(-1);
        _repository.AddPageAsync(dto, Arg.Is<string>(s => s.StartsWith("duplicate-")), 0)
            .Returns(ci => MakePage(id: 1, title: "Duplicate", slug: ci.Arg<string>()));
        _repository.GetByIdAsync(1).Returns(MakePage(id: 1, title: "Duplicate", slug: "duplicate-123"));

        await _sut.CreatePageAsync(dto);

        await _repository.Received(1).AddPageAsync(dto,
            Arg.Is<string>(s => s.StartsWith("duplicate-") && s.Length > "duplicate-".Length), 0);
    }

    // --- UpdatePageAsync ---

    [Fact]
    public async Task UpdatePageAsync_UpdatesAndCreatesVersion()
    {
        var dto = new UpdateWikiPageDto { Title = "Updated Title", Content = "Updated content" };
        var updated = MakePage(id: 5, title: "Updated Title", slug: "updated-title");

        _repository.GetMaxVersionNumberAsync(5).Returns(3);
        _repository.GetByIdAsync(5).Returns(updated);

        var result = await _sut.UpdatePageAsync(5, dto);

        Assert.Equal(5, result.Id);
        await _repository.Received(1).UpdatePageAsync(5, "Updated Title", "Updated content", "updated-title");
        await _repository.Received(1).AddVersionAsync(5, "Updated Title", "Updated content", 4);
    }

    [Fact]
    public async Task UpdatePageAsync_Throws_WhenPageNotFoundAfterUpdate()
    {
        var dto = new UpdateWikiPageDto { Title = "Title", Content = "Content" };

        _repository.GetMaxVersionNumberAsync(5).Returns(1);
        _repository.GetByIdAsync(5).ReturnsNull();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UpdatePageAsync(5, dto));
    }

    // --- DeletePageAsync ---

    [Fact]
    public async Task DeletePageAsync_CallsSoftDeleteOnRepository()
    {
        await _sut.DeletePageAsync(7);

        await _repository.Received(1).SoftDeleteRecursiveAsync(7);
    }

    // --- MovePageAsync ---

    [Fact]
    public async Task MovePageAsync_MovesAndReordersSiblings()
    {
        var page = MakePage(id: 3, title: "Page", slug: "page", parentId: 1);
        _repository.GetByIdAsync(3).Returns(page);

        await _sut.MovePageAsync(3, 2, 0);

        await _repository.Received(1).MovePageAsync(3, 2, 0);
        await _repository.Received(1).ReorderSiblingsAsync(2, 3, 0);
        await _repository.Received(1).ReorderSiblingsSequentialAsync(1, 3);
        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task MovePageAsync_SkipsOldParentReorder_WhenParentUnchanged()
    {
        var page = MakePage(id: 3, title: "Page", slug: "page", parentId: 2);
        _repository.GetByIdAsync(3).Returns(page);

        await _sut.MovePageAsync(3, 2, 1);

        await _repository.DidNotReceive().ReorderSiblingsSequentialAsync(Arg.Any<int?>(), Arg.Any<int>());
    }

    [Fact]
    public async Task MovePageAsync_Throws_WhenPageNotFound()
    {
        _repository.GetByIdAsync(99).ReturnsNull();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.MovePageAsync(99, null, 0));
    }

    // --- GetPageVersionsAsync ---

    [Fact]
    public async Task GetPageVersionsAsync_ReturnsVersionsWithRenderedHtml()
    {
        var versions = new List<WikiPageVersionDto>
        {
            new() { Id = 1, VersionNumber = 1, Title = "V1", Content = "Content 1", CreatedAt = DateTime.UtcNow },
            new() { Id = 2, VersionNumber = 2, Title = "V2", Content = "Content 2", CreatedAt = DateTime.UtcNow }
        };
        _repository.GetVersionsByPageIdAsync(5).Returns(versions);

        var result = await _sut.GetPageVersionsAsync(5);

        Assert.Equal(2, result.Count);
        Assert.Equal("<p>Content 1</p>", result[0].ContentHtml);
        Assert.Equal("<p>Content 2</p>", result[1].ContentHtml);
    }

    // --- RevertToVersionAsync ---

    [Fact]
    public async Task RevertToVersionAsync_RevertsAndCreatesNewVersion()
    {
        var version = new WikiPageVersionDto
        {
            Id = 3, VersionNumber = 1, Title = "Old Title", Content = "Old content"
        };
        var reverted = MakePage(id: 10, title: "Old Title", slug: "old-title");

        _repository.GetVersionByIdAsync(3).Returns(version);
        _repository.GetMaxVersionNumberAsync(10).Returns(2);
        _repository.GetByIdAsync(10).Returns(reverted);

        var result = await _sut.RevertToVersionAsync(10, 3);

        Assert.Equal(10, result.Id);
        await _repository.Received(1).UpdatePageAsync(10, "Old Title", "Old content", "old-title");
        await _repository.Received(1).AddVersionAsync(10, "Old Title", "Old content", 3);
    }

    [Fact]
    public async Task RevertToVersionAsync_Throws_WhenVersionNotFound()
    {
        _repository.GetVersionByIdAsync(999).ReturnsNull();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RevertToVersionAsync(1, 999));
    }

    [Fact]
    public async Task RevertToVersionAsync_Throws_WhenPageNotFoundAfterRevert()
    {
        var version = new WikiPageVersionDto
        {
            Id = 3, VersionNumber = 1, Title = "Title", Content = "Content"
        };

        _repository.GetVersionByIdAsync(3).Returns(version);
        _repository.GetMaxVersionNumberAsync(10).Returns(1);
        _repository.GetByIdAsync(10).ReturnsNull();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RevertToVersionAsync(10, 3));
    }

    // --- Depth limits ---

    [Fact]
    public async Task GetNavigationTreeAsync_TruncatesAtMaxDepth()
    {
        // Build a chain 12 levels deep (exceeds MaxDepth of 10)
        var pages = new List<WikiPageDto>();
        for (var i = 1; i <= 12; i++)
        {
            pages.Add(MakePage(
                id: i,
                title: $"Level {i}",
                slug: $"level-{i}",
                parentId: i == 1 ? null : i - 1,
                sortOrder: 0));
        }

        _repository.GetAllOrderedAsync().Returns(pages);

        var result = await _sut.GetNavigationTreeAsync();

        // Walk the tree and count depth
        var depth = 0;
        var node = result[0];
        while (node.Children.Count > 0)
        {
            depth++;
            node = node.Children[0];
        }

        Assert.True(depth < 12, $"Tree should be truncated but reached depth {depth}");
        Assert.Equal(9, depth); // 0-indexed root + 9 children = 10 levels (MaxDepth)
    }

    [Fact]
    public async Task GetPageByIdAsync_BuildsSlugPath_TruncatesAtMaxDepth()
    {
        // Build a chain 12 levels deep
        for (var i = 1; i <= 12; i++)
        {
            var page = MakePage(
                id: i,
                title: $"Level {i}",
                slug: $"level-{i}",
                parentId: i == 1 ? null : i - 1);
            _repository.GetByIdAsync(i).Returns(page);
        }

        var result = await _sut.GetPageByIdAsync(12);

        Assert.NotNull(result);
        var segments = result.SlugPath.Split('/');
        // MaxDepth of 10 parent lookups + the page itself = at most 11 segments
        Assert.True(segments.Length <= 11,
            $"Slug path should be capped but had {segments.Length} segments");
    }

    // --- Helpers ---

    private static WikiPageDto MakePage(
        int id = 1,
        string title = "Test Page",
        string slug = "test-page",
        string? content = "Test content",
        int? parentId = null,
        int sortOrder = 0) => new()
    {
        Id = id,
        Title = title,
        Slug = slug,
        Content = content,
        ParentId = parentId,
        SortOrder = sortOrder
    };
}
