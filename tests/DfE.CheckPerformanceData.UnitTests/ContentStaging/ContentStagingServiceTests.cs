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
    private static readonly Guid GuidMissing = new("dddddddd-dddd-dddd-dddd-dddddddddddd");

    public ContentStagingServiceTests()
    {
        _sut = new ContentStagingService(_wiki, _wikiRepo, _blockRepo);
        _wikiRepo.GetAllOrderedAsync().Returns([]);
        _blockRepo.GetAllAsync().Returns([]);
        // By default nothing collides on create, so creates proceed.
        _wikiRepo.SlugExistsAsync(Arg.Any<string>(), Arg.Any<int?>()).Returns(false);
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
    public async Task ExportAsync_CarriesGuidIdentityAndParentGuid_NoSlugPaths()
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
        Assert.Equal(GuidParent, child.ParentId);   // parentage by GUID
        Assert.Equal("Parent", parent.Title);
        Assert.Equal("p", parent.Content);
    }

    [Fact]
    public async Task ExportAsync_OrdersSiblingsBySortOrder_AndCarriesSortOrder()
    {
        _wikiRepo.GetAllOrderedAsync().Returns(
        [
            Page(1, "alpha", "Alpha", parentId: null, content: "a", sortOrder: 2, contentId: GuidA),
            Page(2, "beta", "Beta", parentId: null, content: "b", sortOrder: 0, contentId: GuidB),
            Page(3, "gamma", "Gamma", parentId: null, content: "g", sortOrder: 1, contentId: GuidParent)
        ]);

        var bundle = await _sut.ExportAsync();

        Assert.Equal(["Beta", "Gamma", "Alpha"], bundle.WikiPages.Select(p => p.Title));
        Assert.Equal([0, 1, 2], bundle.WikiPages.Select(p => p.SortOrder));
    }

    [Fact]
    public async Task ExportAsync_WalksDepthFirst_ChildImmediatelyAfterParent()
    {
        _wikiRepo.GetAllOrderedAsync().Returns(
        [
            Page(1, "first", "First", parentId: null, content: "1", sortOrder: 0, contentId: GuidA),
            Page(2, "second", "Second", parentId: null, content: "2", sortOrder: 1, contentId: GuidB),
            Page(3, "kid", "Kid", parentId: 1, content: "k", sortOrder: 0, contentId: GuidParent)
        ]);

        var bundle = await _sut.ExportAsync();

        Assert.Equal(["First", "Kid", "Second"], bundle.WikiPages.Select(p => p.Title));
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
        Assert.Equal(GuidB, bundle.ContentBlocks[0].Id);
    }

    [Fact]
    public async Task ExportAsync_WithSelection_IncludesOnlySelectedPagesAndBlocks()
    {
        _wikiRepo.GetAllOrderedAsync().Returns(
        [
            Page(1, "alpha", "Alpha", parentId: null, content: "a", contentId: GuidA),
            Page(2, "beta", "Beta", parentId: null, content: "b", contentId: GuidB)
        ]);
        _blockRepo.GetAllAsync().Returns([Block(1, "footer", "Content", "f", contentId: GuidParent)]);

        var bundle = await _sut.ExportAsync(new ContentExportSelection(new HashSet<Guid> { GuidA }, new HashSet<Guid>()));

        Assert.Equal([GuidA], bundle.WikiPages.Select(p => p.Id));
        Assert.Empty(bundle.ContentBlocks);
    }

    [Fact]
    public async Task ExportAsync_WithSelection_IncludesAncestorsOfSelectedPage()
    {
        _wikiRepo.GetAllOrderedAsync().Returns(
        [
            Page(1, "parent", "Parent", parentId: null, content: "p", contentId: GuidParent),
            Page(2, "child", "Child", parentId: 1, content: "c", contentId: GuidA)
        ]);

        var bundle = await _sut.ExportAsync(new ContentExportSelection(new HashSet<Guid> { GuidA }, new HashSet<Guid>()));

        Assert.Equal([GuidParent, GuidA], bundle.WikiPages.Select(p => p.Id));
    }

    // --- Preview (GUID-only matching) ---

    [Fact]
    public async Task PreviewAsync_NewItems_MarkedNotExisting()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { Id = GuidA, Title = "Alpha" }],
            ContentBlocks = [new() { Id = GuidB, Key = "footer", BlockType = "Content", Value = "f" }]
        };
        _wikiRepo.GetByContentIdAsync(Arg.Any<Guid>()).ReturnsNull();
        _blockRepo.GetByContentIdAsync(Arg.Any<Guid>()).ReturnsNull();

        var preview = await _sut.PreviewAsync(bundle);

        Assert.False(preview.Pages.Single().Exists);
        Assert.False(preview.Blocks.Single().Exists);
        Assert.Equal(2, preview.NewCount);
        Assert.Equal(0, preview.CollisionCount);
    }

    [Fact]
    public async Task PreviewAsync_GuidCollision_ShowsRenameInDescription_AndVirtualPath()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { Id = GuidA, Title = "B doc" }]
        };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 99, ContentId = GuidA, Title = "C doc" });

        var preview = await _sut.PreviewAsync(bundle);

        var item = preview.Pages.Single();
        Assert.True(item.Exists);
        Assert.Equal("b-doc", item.Detail);            // virtual slug path from the title
        Assert.Contains("C doc", item.ExistingDescription);
    }

    [Fact]
    public async Task PreviewAsync_BuildsVirtualSlugPathFromParentChain()
    {
        var bundle = new ContentBundle
        {
            WikiPages =
            [
                new() { Id = GuidParent, Title = "Parent" },
                new() { Id = GuidA, ParentId = GuidParent, Title = "Child Page" }
            ]
        };
        _wikiRepo.GetByContentIdAsync(Arg.Any<Guid>()).ReturnsNull();

        var preview = await _sut.PreviewAsync(bundle);

        var child = preview.Pages.Single(p => p.Title == "Child Page");
        Assert.Equal("parent/child-page", child.Detail);
    }

    [Fact]
    public async Task PreviewAsync_UnknownParent_MarksItemBlocked()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { Id = GuidA, ParentId = GuidMissing, Title = "Orphan" }]
        };
        _wikiRepo.GetByContentIdAsync(Arg.Any<Guid>()).ReturnsNull();

        var preview = await _sut.PreviewAsync(bundle);

        var item = preview.Pages.Single();
        Assert.True(item.ParentMissing);
        Assert.False(item.Exists);
        Assert.Equal(1, preview.BlockedCount);
        Assert.Equal(0, preview.NewCount);
    }

    // --- Import: create / skip / replace, GUID-only ---

    [Fact]
    public async Task ImportAsync_CreatesMissingPages_ResolvingParentByGuid_PersistingIdentity()
    {
        var bundle = new ContentBundle
        {
            WikiPages =
            [
                new() { Id = GuidA, ParentId = GuidParent, Title = "Child", Content = "c" },
                new() { Id = GuidParent, ParentId = null, Title = "Parent", Content = "p" }
            ]
        };
        _wikiRepo.GetByContentIdAsync(Arg.Any<Guid>()).ReturnsNull();
        _wiki.CreatePageAsync(Arg.Any<CreateWikiPageDto>()).Returns(ci =>
        {
            var dto = ci.Arg<CreateWikiPageDto>();
            return new WikiPageDto { Id = dto.Title == "Parent" ? 10 : 11, ParentId = dto.ParentId };
        });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(2, result.WikiPagesCreated);
        Assert.Empty(result.Errors);
        await _wiki.Received(1).CreatePageAsync(Arg.Is<CreateWikiPageDto>(
            d => d.Title == "Parent" && d.ParentId == null && d.ContentId == GuidParent));
        await _wiki.Received(1).CreatePageAsync(Arg.Is<CreateWikiPageDto>(
            d => d.Title == "Child" && d.ParentId == 10 && d.ContentId == GuidA));
    }

    [Fact]
    public async Task ImportAsync_PassesBundleSortOrder_WhenCreatingPage()
    {
        var bundle = new ContentBundle { WikiPages = [new() { Id = GuidA, Title = "Alpha", Content = "a", SortOrder = 5 }] };
        _wikiRepo.GetByContentIdAsync(Arg.Any<Guid>()).ReturnsNull();
        _wiki.CreatePageAsync(Arg.Any<CreateWikiPageDto>()).Returns(new WikiPageDto { Id = 1 });

        await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        await _wiki.Received(1).CreatePageAsync(Arg.Is<CreateWikiPageDto>(d => d.SortOrder == 5));
    }

    [Fact]
    public async Task ImportAsync_PerItemDecision_OverridesGlobalMode()
    {
        var bundle = new ContentBundle
        {
            WikiPages =
            [
                new() { Id = GuidA, Title = "A", Content = "a" },
                new() { Id = GuidB, Title = "B", Content = "b" }
            ]
        };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 10, ContentId = GuidA });
        _wikiRepo.GetByContentIdAsync(GuidB).Returns(new WikiPageDto { Id = 11, ContentId = GuidB });
        _wiki.UpdatePageAsync(Arg.Any<int>(), Arg.Any<UpdateWikiPageDto>()).Returns(new WikiPageDto());

        var decisions = new Dictionary<Guid, ContentImportMode> { [GuidA] = ContentImportMode.Replace };
        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip, decisions);

        Assert.Equal(1, result.WikiPagesUpdated);
        Assert.Equal(1, result.WikiPagesSkipped);
        await _wiki.Received(1).UpdatePageAsync(10, Arg.Any<UpdateWikiPageDto>());
        await _wiki.DidNotReceive().UpdatePageAsync(11, Arg.Any<UpdateWikiPageDto>());
    }

    [Fact]
    public async Task ImportAsync_GlobalFail_ButItemResolved_DoesNotAbort()
    {
        var bundle = new ContentBundle { WikiPages = [new() { Id = GuidA, Title = "A", Content = "a" }] };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 10, ContentId = GuidA });
        _wiki.UpdatePageAsync(10, Arg.Any<UpdateWikiPageDto>()).Returns(new WikiPageDto { Id = 10 });

        var decisions = new Dictionary<Guid, ContentImportMode> { [GuidA] = ContentImportMode.Replace };
        var result = await _sut.ImportAsync(bundle, ContentImportMode.Fail, decisions);

        Assert.Equal(1, result.WikiPagesUpdated);
    }

    [Fact]
    public async Task ImportAsync_GlobalFail_UnresolvedCollision_Aborts()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { Id = GuidA, Title = "A" }, new() { Id = GuidB, Title = "B" }]
        };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 10, ContentId = GuidA });
        _wikiRepo.GetByContentIdAsync(GuidB).Returns(new WikiPageDto { Id = 11, ContentId = GuidB });

        var decisions = new Dictionary<Guid, ContentImportMode> { [GuidA] = ContentImportMode.Replace };

        await Assert.ThrowsAsync<ContentImportConflictException>(
            () => _sut.ImportAsync(bundle, ContentImportMode.Fail, decisions));
        await _wiki.DidNotReceive().UpdatePageAsync(Arg.Any<int>(), Arg.Any<UpdateWikiPageDto>());
    }

    [Fact]
    public async Task ImportAsync_Skip_LeavesExistingPageUntouched()
    {
        var bundle = new ContentBundle { WikiPages = [new() { Id = GuidA, Title = "Parent", Content = "p" }] };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 10, ContentId = GuidA });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(1, result.WikiPagesSkipped);
        await _wiki.DidNotReceive().UpdatePageAsync(Arg.Any<int>(), Arg.Any<UpdateWikiPageDto>());
        await _wiki.DidNotReceive().CreatePageAsync(Arg.Any<CreateWikiPageDto>());
    }

    [Fact]
    public async Task ImportAsync_Replace_UpdatesExistingPage_MatchedByGuid()
    {
        var bundle = new ContentBundle { WikiPages = [new() { Id = GuidA, Title = "Parent", Content = "new" }] };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 10, ContentId = GuidA });
        _wiki.UpdatePageAsync(10, Arg.Any<UpdateWikiPageDto>()).Returns(new WikiPageDto { Id = 10 });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Equal(1, result.WikiPagesUpdated);
        await _wiki.Received(1).UpdatePageAsync(10, Arg.Is<UpdateWikiPageDto>(d => d.Title == "Parent" && d.Content == "new"));
    }

    [Fact]
    public async Task ImportAsync_Replace_RepositionsExistingPage_ViaSortOrder()
    {
        var bundle = new ContentBundle { WikiPages = [new() { Id = GuidA, Title = "Parent", Content = "new", SortOrder = 7 }] };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 10, ContentId = GuidA });
        _wiki.UpdatePageAsync(10, Arg.Any<UpdateWikiPageDto>()).Returns(new WikiPageDto { Id = 10 });

        await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        await _wiki.Received(1).UpdatePageAsync(10, Arg.Is<UpdateWikiPageDto>(d => d.SortOrder == 7));
    }

    [Fact]
    public async Task ImportAsync_Replace_MatchesRenamedPageByGuid_RestoringTitle()
    {
        // The bundle's "B" lands on the target's renamed "C" — same GUID, so it's recognised.
        var bundle = new ContentBundle { WikiPages = [new() { Id = GuidA, Title = "B", Content = "from-source" }] };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 99, ContentId = GuidA, Title = "C" });
        _wiki.UpdatePageAsync(99, Arg.Any<UpdateWikiPageDto>()).Returns(new WikiPageDto { Id = 99 });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Equal(1, result.WikiPagesUpdated);
        await _wiki.Received(1).UpdatePageAsync(99, Arg.Is<UpdateWikiPageDto>(d => d.Title == "B" && d.Content == "from-source"));
    }

    [Fact]
    public async Task ImportAsync_UnknownParent_ReportsError_AndCreatesNoOrphan()
    {
        var bundle = new ContentBundle
        {
            WikiPages = [new() { Id = GuidA, ParentId = GuidMissing, Title = "Child", Content = "c" }]
        };
        _wikiRepo.GetByContentIdAsync(Arg.Any<Guid>()).ReturnsNull();

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Single(result.Errors);
        Assert.Equal(0, result.WikiPagesCreated);
        await _wiki.DidNotReceive().CreatePageAsync(Arg.Any<CreateWikiPageDto>());
    }

    [Fact]
    public async Task ImportAsync_NewPage_SlugClashWithDifferentName_ReportsError()
    {
        var bundle = new ContentBundle { WikiPages = [new() { Id = GuidA, Title = "Alpha", Content = "a" }] };
        _wikiRepo.GetByContentIdAsync(GuidA).ReturnsNull();             // unknown identity -> would create
        _wikiRepo.SlugExistsAsync("alpha", (int?)null).Returns(true);   // but the slug is taken

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Single(result.Errors);
        await _wiki.DidNotReceive().CreatePageAsync(Arg.Any<CreateWikiPageDto>());
    }

    // --- Import: Fail mode ---

    [Fact]
    public async Task ImportAsync_Fail_WhenPageExists_Throws_AndMakesNoChanges()
    {
        var bundle = new ContentBundle { WikiPages = [new() { Id = GuidA, Title = "Parent", Content = "p" }] };
        _wikiRepo.GetByContentIdAsync(GuidA).Returns(new WikiPageDto { Id = 10, ContentId = GuidA });

        await Assert.ThrowsAsync<ContentImportConflictException>(
            () => _sut.ImportAsync(bundle, ContentImportMode.Fail));
        await _wiki.DidNotReceive().CreatePageAsync(Arg.Any<CreateWikiPageDto>());
        await _wiki.DidNotReceive().UpdatePageAsync(Arg.Any<int>(), Arg.Any<UpdateWikiPageDto>());
    }

    [Fact]
    public async Task ImportAsync_Fail_WhenBlockExists_Throws()
    {
        var bundle = new ContentBundle { ContentBlocks = [new() { Id = GuidB, Key = "footer", BlockType = "Content", Value = "v" }] };
        _blockRepo.GetByContentIdAsync(GuidB).Returns(new ContentBlockDto { Id = 1, ContentId = GuidB, Key = "footer" });

        await Assert.ThrowsAsync<ContentImportConflictException>(
            () => _sut.ImportAsync(bundle, ContentImportMode.Fail));
        await _blockRepo.DidNotReceive().AddBlockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>());
    }

    // --- Import: content blocks (GUID-only) ---

    [Fact]
    public async Task ImportAsync_CreatesMissingBlock_WithIdentity()
    {
        var bundle = new ContentBundle { ContentBlocks = [new() { Id = GuidB, Key = "footer", BlockType = "Content", Value = "v" }] };
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
        var bundle = new ContentBundle { ContentBlocks = [new() { Id = GuidB, Key = "footer", BlockType = "Content", Value = "v" }] };
        _blockRepo.GetByContentIdAsync(GuidB).Returns(new ContentBlockDto { Id = 1, ContentId = GuidB, Key = "footer" });

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(1, result.ContentBlocksSkipped);
        await _blockRepo.DidNotReceive().UpdateForStagingAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>());
        await _blockRepo.DidNotReceive().AddBlockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task ImportAsync_Replace_ExistingBlock_MatchedByGuid_OverwrittenInPlace()
    {
        var bundle = new ContentBundle { ContentBlocks = [new() { Id = GuidB, Key = "footer", BlockType = "Content", Value = "v2" }] };
        _blockRepo.GetByContentIdAsync(GuidB).Returns(new ContentBlockDto { Id = 1, ContentId = GuidB, Key = "footer", Value = "v1" });
        _blockRepo.GetMaxVersionNumberAsync(1).Returns(2);

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Equal(1, result.ContentBlocksUpdated);
        await _blockRepo.Received(1).UpdateForStagingAsync(1, "footer", "Content", "v2", GuidB);
        await _blockRepo.Received(1).AddVersionAsync(1, "v2", 3);
    }

    [Fact]
    public async Task ImportAsync_NewBlock_KeyClashWithDifferentIdentity_ReportsError()
    {
        var bundle = new ContentBundle { ContentBlocks = [new() { Id = GuidB, Key = "footer", BlockType = "Content", Value = "v" }] };
        _blockRepo.GetByContentIdAsync(GuidB).ReturnsNull();                                    // unknown identity
        _blockRepo.GetByKeyAsync("footer").Returns(new ContentBlockDto { Id = 1, ContentId = GuidA, Key = "footer" }); // key taken

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Single(result.Errors);
        await _blockRepo.DidNotReceive().AddBlockAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>());
    }
}
