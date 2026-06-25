using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.Wiki;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

public class ContentStagingServiceTests
{
    private readonly IWikiService _wiki = Substitute.For<IWikiService>();
    private readonly IWikiRepository _wikiRepo = Substitute.For<IWikiRepository>();
    private readonly IContentBlockService _blocks = Substitute.For<IContentBlockService>();
    private readonly IContentBlockRepository _blockRepo = Substitute.For<IContentBlockRepository>();
    private readonly ContentStagingService _sut;

    public ContentStagingServiceTests()
    {
        _sut = new ContentStagingService(_wiki, _wikiRepo, _blocks, _blockRepo);
        _wikiRepo.GetAllOrderedAsync().Returns([]);
        _blockRepo.GetAllAsync().Returns([]);
    }

    private static WikiPageDto Page(int id, string slug, string title, int? parentId, string? content, int sortOrder = 0) =>
        new() { Id = id, Slug = slug, Title = title, ParentId = parentId, Content = content, SortOrder = sortOrder };

    private static ContentBlockDto Block(int id, string key, string type, string value) =>
        new() { Id = id, Key = key, BlockType = type, Value = value };

    // --- Export ---

    [Fact]
    public async Task ExportAsync_SetsCurrentSchema()
    {
        var bundle = await _sut.ExportAsync();
        Assert.Equal(ContentBundle.CurrentSchema, bundle.Schema);
    }

    [Fact]
    public async Task ExportAsync_BuildsSlugPaths_ParentBeforeChild_WithContent()
    {
        _wikiRepo.GetAllOrderedAsync().Returns(
        [
            Page(2, "child", "Child", parentId: 1, content: "child body"),
            Page(1, "parent", "Parent", parentId: null, content: "parent body")
        ]);

        var bundle = await _sut.ExportAsync();

        Assert.Equal(2, bundle.WikiPages.Count);
        var parent = bundle.WikiPages[0];
        var child = bundle.WikiPages[1];

        Assert.Equal("parent", parent.SlugPath);
        Assert.Equal("", parent.ParentSlugPath);
        Assert.Equal("parent body", parent.Content);

        Assert.Equal("parent/child", child.SlugPath);
        Assert.Equal("parent", child.ParentSlugPath);
        Assert.Equal("child body", child.Content);
    }

    [Fact]
    public async Task ExportAsync_OrdersSiblingsBySortOrder_NotAlphabetically_AndCarriesSortOrder()
    {
        // SortOrder deliberately diverges from alphabetical slug order so the assertion proves
        // the export honours SortOrder rather than falling back to slug sorting.
        _wikiRepo.GetAllOrderedAsync().Returns(
        [
            Page(1, "alpha", "Alpha", parentId: null, content: "a", sortOrder: 2),
            Page(2, "beta", "Beta", parentId: null, content: "b", sortOrder: 0),
            Page(3, "gamma", "Gamma", parentId: null, content: "g", sortOrder: 1)
        ]);

        var bundle = await _sut.ExportAsync();

        Assert.Equal(["beta", "gamma", "alpha"], bundle.WikiPages.Select(p => p.SlugPath));
        Assert.Equal([0, 1, 2], bundle.WikiPages.Select(p => p.SortOrder));
    }

    [Fact]
    public async Task ExportAsync_WalksDepthFirst_ChildImmediatelyAfterParent()
    {
        _wikiRepo.GetAllOrderedAsync().Returns(
        [
            Page(1, "first", "First", parentId: null, content: "1", sortOrder: 0),
            Page(2, "second", "Second", parentId: null, content: "2", sortOrder: 1),
            Page(3, "kid", "Kid", parentId: 1, content: "k", sortOrder: 0)
        ]);

        var bundle = await _sut.ExportAsync();

        // Pre-order: first, first/kid, second — the child sits directly under its parent.
        Assert.Equal(["first", "first/kid", "second"], bundle.WikiPages.Select(p => p.SlugPath));
    }

    [Fact]
    public async Task ExportAsync_IncludesContentBlocks_OrderedByKey()
    {
        _blockRepo.GetAllAsync().Returns(
        [
            Block(1, "zeta", "Content", "z"),
            Block(2, "alpha", "Footer", "a")
        ]);

        var bundle = await _sut.ExportAsync();

        Assert.Equal(["alpha", "zeta"], bundle.ContentBlocks.Select(b => b.Key));
        Assert.Equal("Footer", bundle.ContentBlocks[0].BlockType);
    }

    // --- Import: wiki create / skip / replace ---

    [Fact]
    public async Task ImportAsync_Skip_CreatesMissingPages_ResolvingParentBySlugPath()
    {
        var bundle = new ContentBundle
        {
            WikiPages =
            [
                new() { SlugPath = "parent/child", ParentSlugPath = "parent", Slug = "child", Title = "Child", Content = "c" },
                new() { SlugPath = "parent", ParentSlugPath = "", Slug = "parent", Title = "Parent", Content = "p" }
            ]
        };

        _wikiRepo.GetBySlugAndParentAsync("parent", (int?)null).ReturnsNull();
        _wikiRepo.GetBySlugAndParentAsync("child", 10).ReturnsNull();
        _wiki.CreatePageAsync(Arg.Any<CreateWikiPageDto>()).Returns(ci =>
        {
            var dto = ci.Arg<CreateWikiPageDto>();
            return new WikiPageDto { Id = dto.Title == "Parent" ? 10 : 11, ParentId = dto.ParentId };
        });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(2, result.WikiPagesCreated);
        await _wiki.Received(1).CreatePageAsync(Arg.Is<CreateWikiPageDto>(d => d.Title == "Child" && d.ParentId == 10));
        await _wiki.Received(1).CreatePageAsync(Arg.Is<CreateWikiPageDto>(d => d.Title == "Parent" && d.ParentId == null));
    }

    [Fact]
    public async Task ImportAsync_PassesBundleSortOrder_WhenCreatingPage()
    {
        var bundle = new ContentBundle
        {
            WikiPages =
            [
                new() { SlugPath = "alpha", ParentSlugPath = "", Slug = "alpha", Title = "Alpha", Content = "a", SortOrder = 5 }
            ]
        };
        _wikiRepo.GetBySlugAndParentAsync("alpha", (int?)null).ReturnsNull();
        _wiki.CreatePageAsync(Arg.Any<CreateWikiPageDto>()).Returns(new WikiPageDto { Id = 1 });

        await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        await _wiki.Received(1).CreatePageAsync(Arg.Is<CreateWikiPageDto>(d => d.SortOrder == 5));
    }

    [Fact]
    public async Task ImportAsync_Skip_LeavesExistingPageUntouched()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { SlugPath = "parent", ParentSlugPath = "", Slug = "parent", Title = "Parent", Content = "p" }]
        };
        _wikiRepo.GetBySlugAndParentAsync("parent", (int?)null).Returns(new WikiPageDto { Id = 10 });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(1, result.WikiPagesSkipped);
        Assert.Equal(0, result.WikiPagesCreated);
        await _wiki.DidNotReceive().UpdatePageAsync(Arg.Any<int>(), Arg.Any<UpdateWikiPageDto>());
        await _wiki.DidNotReceive().CreatePageAsync(Arg.Any<CreateWikiPageDto>());
    }

    [Fact]
    public async Task ImportAsync_Replace_UpdatesExistingPage()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { SlugPath = "parent", ParentSlugPath = "", Slug = "parent", Title = "Parent", Content = "new" }]
        };
        _wikiRepo.GetBySlugAndParentAsync("parent", (int?)null).Returns(new WikiPageDto { Id = 10 });
        _wiki.UpdatePageAsync(10, Arg.Any<UpdateWikiPageDto>()).Returns(new WikiPageDto { Id = 10 });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Equal(1, result.WikiPagesUpdated);
        await _wiki.Received(1).UpdatePageAsync(10, Arg.Is<UpdateWikiPageDto>(d => d.Title == "Parent" && d.Content == "new"));
    }

    [Fact]
    public async Task ImportAsync_Replace_RepositionsExistingPage_ViaSortOrder()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { SlugPath = "parent", ParentSlugPath = "", Slug = "parent", Title = "Parent", Content = "new", SortOrder = 7 }]
        };
        _wikiRepo.GetBySlugAndParentAsync("parent", (int?)null).Returns(new WikiPageDto { Id = 10 });
        _wiki.UpdatePageAsync(10, Arg.Any<UpdateWikiPageDto>()).Returns(new WikiPageDto { Id = 10 });

        await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        await _wiki.Received(1).UpdatePageAsync(10, Arg.Is<UpdateWikiPageDto>(d => d.SortOrder == 7));
    }

    [Fact]
    public async Task ImportAsync_MissingParent_SkipsChild_WithWarning()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { SlugPath = "orphan/child", ParentSlugPath = "orphan", Slug = "child", Title = "Child", Content = "c" }]
        };
        _wiki.GetPageBySlugPathAsync("orphan").ReturnsNull();

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(1, result.WikiPagesSkipped);
        Assert.Single(result.Warnings);
        await _wiki.DidNotReceive().CreatePageAsync(Arg.Any<CreateWikiPageDto>());
    }

    // --- Import: Fail mode ---

    [Fact]
    public async Task ImportAsync_Fail_WhenPageExists_Throws_AndMakesNoChanges()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { SlugPath = "parent", ParentSlugPath = "", Slug = "parent", Title = "Parent", Content = "p" }]
        };
        _wikiRepo.GetBySlugAndParentAsync("parent", (int?)null).Returns(new WikiPageDto { Id = 10 });

        await Assert.ThrowsAsync<ContentImportConflictException>(
            () => _sut.ImportAsync(bundle, ContentImportMode.Fail));

        await _wiki.DidNotReceive().CreatePageAsync(Arg.Any<CreateWikiPageDto>());
        await _wiki.DidNotReceive().UpdatePageAsync(Arg.Any<int>(), Arg.Any<UpdateWikiPageDto>());
    }

    [Fact]
    public async Task ImportAsync_Fail_WhenBlockExists_Throws()
    {
        var bundle = new ContentBundle
        {
            ContentBlocks = [new() { Key = "footer", BlockType = "Content", Value = "v" }]
        };
        _blocks.GetByKeyAsync("footer").Returns(new ContentBlockDto { Id = 1, Key = "footer" });

        await Assert.ThrowsAsync<ContentImportConflictException>(
            () => _sut.ImportAsync(bundle, ContentImportMode.Fail));

        await _blocks.DidNotReceive().SaveAsync(Arg.Any<SaveContentBlockDto>());
    }

    // --- Import: content blocks ---

    [Fact]
    public async Task ImportAsync_CreatesMissingBlock()
    {
        var bundle = new ContentBundle
        {
            ContentBlocks = [new() { Key = "footer", BlockType = "Content", Value = "v" }]
        };
        _blocks.GetByKeyAsync("footer").ReturnsNull();
        _blocks.SaveAsync(Arg.Any<SaveContentBlockDto>()).Returns(new ContentBlockDto { Id = 1, Key = "footer" });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(1, result.ContentBlocksCreated);
        await _blocks.Received(1).SaveAsync(Arg.Is<SaveContentBlockDto>(d => d.Key == "footer" && d.Value == "v"));
    }

    [Fact]
    public async Task ImportAsync_Skip_ExistingBlock_NotSaved()
    {
        var bundle = new ContentBundle
        {
            ContentBlocks = [new() { Key = "footer", BlockType = "Content", Value = "v" }]
        };
        _blocks.GetByKeyAsync("footer").Returns(new ContentBlockDto { Id = 1, Key = "footer" });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(1, result.ContentBlocksSkipped);
        await _blocks.DidNotReceive().SaveAsync(Arg.Any<SaveContentBlockDto>());
    }

    [Fact]
    public async Task ImportAsync_Replace_ExistingBlock_Saved()
    {
        var bundle = new ContentBundle
        {
            ContentBlocks = [new() { Key = "footer", BlockType = "Content", Value = "v2" }]
        };
        _blocks.GetByKeyAsync("footer").Returns(new ContentBlockDto { Id = 1, Key = "footer", Value = "v1" });
        _blocks.SaveAsync(Arg.Any<SaveContentBlockDto>()).Returns(new ContentBlockDto { Id = 1, Key = "footer" });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Equal(1, result.ContentBlocksUpdated);
        await _blocks.Received(1).SaveAsync(Arg.Is<SaveContentBlockDto>(d => d.Key == "footer" && d.Value == "v2"));
    }
}
