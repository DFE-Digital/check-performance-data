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
    private readonly IContentBlockRepository _blockRepo = Substitute.For<IContentBlockRepository>();
    private readonly ContentStagingService _sut;

    private static readonly Guid GuidA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GuidB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid GuidParent = new("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public ContentStagingServiceTests()
    {
        _sut = new ContentStagingService(_wiki, _wikiRepo, _blockRepo);
        _wikiRepo.GetAllOrderedAsync().Returns([]);
        _blockRepo.GetAllAsync().Returns([]);
        // Run the unit-of-work callback inline so block writes execute under test.
        _blockRepo.ExecuteInTransactionAsync(Arg.Any<Func<Task>>())
            .Returns(ci => ((Func<Task>)ci[0])());
    }

    private static WikiPageDto Page(int id, string slug, string title, int? parentId, string? content,
        int sortOrder = 0, Guid contentId = default) =>
        new() { Id = id, ContentId = contentId, Slug = slug, Title = title, ParentId = parentId, Content = content, SortOrder = sortOrder };

    private static ContentBlockDto Block(int id, string key, string type, string value, Guid contentId = default) =>
        new() { Id = id, ContentId = contentId, Key = key, BlockType = type, Value = value };

    // --- Export ---

    [Fact]
    public async Task ExportAsync_SetsCurrentSchema()
    {
        var bundle = await _sut.ExportAsync();
        Assert.Equal(ContentBundle.CurrentSchema, bundle.Schema);
    }

    [Fact]
    public async Task ExportAsync_CarriesGuidIdentity_AndParentGuid()
    {
        _wikiRepo.GetAllOrderedAsync().Returns(
        [
            Page(2, "child", "Child", parentId: 1, content: "c", contentId: GuidA),
            Page(1, "parent", "Parent", parentId: null, content: "p", contentId: GuidParent)
        ]);

        var bundle = await _sut.ExportAsync();

        var parent = bundle.WikiPages[0];
        var child = bundle.WikiPages[1];

        Assert.Equal(GuidParent, parent.Id);
        Assert.Null(parent.ParentId);
        Assert.Equal(GuidA, child.Id);
        Assert.Equal(GuidParent, child.ParentId);   // parentage carried by GUID, not slug
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

        Assert.Equal(["first", "first/kid", "second"], bundle.WikiPages.Select(p => p.SlugPath));
    }

    [Fact]
    public async Task ExportAsync_IncludesContentBlocks_OrderedByKey_WithGuid()
    {
        _blockRepo.GetAllAsync().Returns(
        [
            Block(1, "zeta", "Content", "z", contentId: GuidA),
            Block(2, "alpha", "Footer", "a", contentId: GuidB)
        ]);

        var bundle = await _sut.ExportAsync();

        Assert.Equal(["alpha", "zeta"], bundle.ContentBlocks.Select(b => b.Key));
        Assert.Equal("Footer", bundle.ContentBlocks[0].BlockType);
        Assert.Equal(GuidB, bundle.ContentBlocks[0].Id);   // identity carried
    }

    // --- Import: wiki create / skip / replace, parentage by GUID ---

    [Fact]
    public async Task ImportAsync_CreatesMissingPages_ResolvingParentByGuid_PersistingIdentity()
    {
        var bundle = new ContentBundle
        {
            WikiPages =
            [
                new() { Id = GuidA, ParentId = GuidParent, SlugPath = "parent/child", Slug = "child", Title = "Child", Content = "c" },
                new() { Id = GuidParent, ParentId = null, SlugPath = "parent", Slug = "parent", Title = "Parent", Content = "p" }
            ]
        };

        _wikiRepo.GetByContentIdAsync(Arg.Any<Guid>()).ReturnsNull();
        _wikiRepo.GetBySlugAndParentAsync(Arg.Any<string>(), Arg.Any<int?>()).ReturnsNull();
        _wiki.CreatePageAsync(Arg.Any<CreateWikiPageDto>()).Returns(ci =>
        {
            var dto = ci.Arg<CreateWikiPageDto>();
            return new WikiPageDto { Id = dto.Title == "Parent" ? 10 : 11, ParentId = dto.ParentId };
        });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(2, result.WikiPagesCreated);
        await _wiki.Received(1).CreatePageAsync(Arg.Is<CreateWikiPageDto>(
            d => d.Title == "Parent" && d.ParentId == null && d.ContentId == GuidParent));
        await _wiki.Received(1).CreatePageAsync(Arg.Is<CreateWikiPageDto>(
            d => d.Title == "Child" && d.ParentId == 10 && d.ContentId == GuidA));
    }

    [Fact]
    public async Task ImportAsync_PassesBundleSortOrder_WhenCreatingPage()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { Id = GuidA, SlugPath = "alpha", Slug = "alpha", Title = "Alpha", Content = "a", SortOrder = 5 }]
        };
        _wikiRepo.GetByContentIdAsync(Arg.Any<Guid>()).ReturnsNull();
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
            WikiPages = [new() { Id = GuidA, SlugPath = "parent", Slug = "parent", Title = "Parent", Content = "p" }]
        };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 10, ContentId = GuidA });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(1, result.WikiPagesSkipped);
        Assert.Equal(0, result.WikiPagesCreated);
        await _wiki.DidNotReceive().UpdatePageAsync(Arg.Any<int>(), Arg.Any<UpdateWikiPageDto>());
        await _wiki.DidNotReceive().CreatePageAsync(Arg.Any<CreateWikiPageDto>());
    }

    [Fact]
    public async Task ImportAsync_Replace_UpdatesExistingPage_MatchedByGuid()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { Id = GuidA, SlugPath = "parent", Slug = "parent", Title = "Parent", Content = "new" }]
        };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 10, ContentId = GuidA });
        _wiki.UpdatePageAsync(10, Arg.Any<UpdateWikiPageDto>()).Returns(new WikiPageDto { Id = 10 });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Equal(1, result.WikiPagesUpdated);
        await _wiki.Received(1).UpdatePageAsync(10, Arg.Is<UpdateWikiPageDto>(d => d.Title == "Parent" && d.Content == "new"));
        // Matched by identity already, so no reconcile needed.
        await _wikiRepo.DidNotReceive().SetContentIdAsync(Arg.Any<int>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task ImportAsync_Replace_RepositionsExistingPage_ViaSortOrder()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { Id = GuidA, SlugPath = "parent", Slug = "parent", Title = "Parent", Content = "new", SortOrder = 7 }]
        };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 10, ContentId = GuidA });
        _wiki.UpdatePageAsync(10, Arg.Any<UpdateWikiPageDto>()).Returns(new WikiPageDto { Id = 10 });

        await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        await _wiki.Received(1).UpdatePageAsync(10, Arg.Is<UpdateWikiPageDto>(d => d.SortOrder == 7));
    }

    [Fact]
    public async Task ImportAsync_Replace_MatchesRenamedPageByGuid_RestoringTitle()
    {
        // The renamed-document case: bundle page B (GuidA) lands on the target's "C", which was a
        // rename of B and so still carries GuidA. Match is by identity, not slug, so C is restored.
        var bundle = new ContentBundle
        {
            WikiPages = [new() { Id = GuidA, SlugPath = "b", Slug = "b", Title = "B", Content = "from-source" }]
        };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 99, ContentId = GuidA, Slug = "c", Title = "C" });
        _wiki.UpdatePageAsync(99, Arg.Any<UpdateWikiPageDto>()).Returns(new WikiPageDto { Id = 99 });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Equal(1, result.WikiPagesUpdated);
        await _wiki.Received(1).UpdatePageAsync(99, Arg.Is<UpdateWikiPageDto>(d => d.Title == "B" && d.Content == "from-source"));
        await _wikiRepo.DidNotReceive().SetContentIdAsync(Arg.Any<int>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task ImportAsync_Replace_SlugClashWithDifferentIdentity_ReconcilesContentId()
    {
        // GUID not found, but a page already occupies the same slug/parent under a different
        // identity: overwrite it and adopt the bundle's identity for future syncs.
        var bundle = new ContentBundle
        {
            WikiPages = [new() { Id = GuidA, SlugPath = "x", Slug = "x", Title = "X", Content = "v" }]
        };
        _wikiRepo.GetByContentIdAsync(GuidA).ReturnsNull();
        _wikiRepo.GetBySlugAndParentAsync("x", (int?)null).Returns(new WikiPageDto { Id = 20, ContentId = GuidB });
        _wiki.UpdatePageAsync(20, Arg.Any<UpdateWikiPageDto>()).Returns(new WikiPageDto { Id = 20 });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Equal(1, result.WikiPagesUpdated);
        await _wiki.Received(1).UpdatePageAsync(20, Arg.Any<UpdateWikiPageDto>());
        await _wikiRepo.Received(1).SetContentIdAsync(20, GuidA);
    }

    [Fact]
    public async Task ImportAsync_MissingParent_SkipsChild_WithWarning()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { Id = GuidA, ParentId = GuidParent, SlugPath = "orphan/child", Slug = "child", Title = "Child", Content = "c" }]
        };
        _wikiRepo.GetByContentIdAsync(Arg.Any<Guid>()).ReturnsNull();

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
            WikiPages = [new() { Id = GuidA, SlugPath = "parent", Slug = "parent", Title = "Parent", Content = "p" }]
        };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 10, ContentId = GuidA });

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
            ContentBlocks = [new() { Id = GuidB, Key = "footer", BlockType = "Content", Value = "v" }]
        };
        _blockRepo.GetByContentIdAsync(GuidB).Returns(new ContentBlockDto { Id = 1, ContentId = GuidB, Key = "footer" });

        await Assert.ThrowsAsync<ContentImportConflictException>(
            () => _sut.ImportAsync(bundle, ContentImportMode.Fail));

        await _blockRepo.DidNotReceive().AddBlockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>());
        await _blockRepo.DidNotReceive().UpdateForStagingAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>());
    }

    // --- Import: content blocks (GUID identity) ---

    [Fact]
    public async Task ImportAsync_CreatesMissingBlock_WithIdentity()
    {
        var bundle = new ContentBundle
        {
            ContentBlocks = [new() { Id = GuidB, Key = "footer", BlockType = "Content", Value = "v" }]
        };
        _blockRepo.GetByContentIdAsync(GuidB).ReturnsNull();
        _blockRepo.GetByKeyAsync("footer").ReturnsNull();
        _blockRepo.AddBlockAsync("footer", "Content", "v", GuidB).Returns(new ContentBlockDto { Id = 1 });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(1, result.ContentBlocksCreated);
        await _blockRepo.Received(1).AddBlockAsync("footer", "Content", "v", GuidB);
    }

    [Fact]
    public async Task ImportAsync_Skip_ExistingBlock_NotWritten()
    {
        var bundle = new ContentBundle
        {
            ContentBlocks = [new() { Id = GuidB, Key = "footer", BlockType = "Content", Value = "v" }]
        };
        _blockRepo.GetByContentIdAsync(GuidB).Returns(new ContentBlockDto { Id = 1, ContentId = GuidB, Key = "footer" });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(1, result.ContentBlocksSkipped);
        await _blockRepo.DidNotReceive().UpdateForStagingAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>());
        await _blockRepo.DidNotReceive().AddBlockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task ImportAsync_Replace_ExistingBlock_MatchedByGuid_OverwrittenInPlace()
    {
        var bundle = new ContentBundle
        {
            ContentBlocks = [new() { Id = GuidB, Key = "footer", BlockType = "Content", Value = "v2" }]
        };
        _blockRepo.GetByContentIdAsync(GuidB).Returns(new ContentBlockDto { Id = 1, ContentId = GuidB, Key = "footer", Value = "v1" });
        _blockRepo.GetMaxVersionNumberAsync(1).Returns(2);

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Equal(1, result.ContentBlocksUpdated);
        await _blockRepo.Received(1).UpdateForStagingAsync(1, "footer", "Content", "v2", GuidB);
        await _blockRepo.Received(1).AddVersionAsync(1, "v2", 3);
    }

    [Fact]
    public async Task ImportAsync_Replace_BlockKeyClashWithDifferentIdentity_ReconcilesIdentity()
    {
        // GUID not found, but a block with the same Key exists under a different identity:
        // overwrite it and adopt the bundle's identity.
        var bundle = new ContentBundle
        {
            ContentBlocks = [new() { Id = GuidB, Key = "footer", BlockType = "Content", Value = "v" }]
        };
        _blockRepo.GetByContentIdAsync(GuidB).ReturnsNull();
        _blockRepo.GetByKeyAsync("footer").Returns(new ContentBlockDto { Id = 1, ContentId = GuidA, Key = "footer" });
        _blockRepo.GetMaxVersionNumberAsync(1).Returns(0);

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Equal(1, result.ContentBlocksUpdated);
        await _blockRepo.Received(1).UpdateForStagingAsync(1, "footer", "Content", "v", GuidB);
    }
}
