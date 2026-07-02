using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

public class ContentStagingServiceTests
{
    private readonly IPageNodeRepository _pages = Substitute.For<IPageNodeRepository>();
    private readonly IContentBlockRepository _blockRepo = Substitute.For<IContentBlockRepository>();
    private readonly ContentStagingService _sut;

    private static readonly Guid GuidA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GuidB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid GuidC = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid GuidD = new("dddddddd-dddd-dddd-dddd-dddddddddddd");

    public ContentStagingServiceTests()
    {
        _sut = new ContentStagingService(_pages, _blockRepo);
        _pages.GetTreeAsync().Returns([]);
        _pages.GetVersionsAsync(Arg.Any<Guid>()).Returns([]);
        _blockRepo.GetAllAsync().Returns([]);
        _blockRepo.ExecuteInTransactionAsync(Arg.Any<Func<Task>>())
            .Returns(ci => ((Func<Task>)ci[0])());
    }

    private static PageNodeTreeItemDto Node(
        Guid id, Guid? parentId, string segment, string title, string pageType = "content",
        int sortOrder = 0, bool hasLive = true, string? path = null) =>
        new()
        {
            Id = id,
            ParentId = parentId,
            Segment = segment,
            Path = path ?? segment,
            SortOrder = sortOrder,
            Title = title,
            PageType = pageType,
            HasLiveVersion = hasLive,
            CreatedDate = new DateTime(2026, 1, 1)
        };

    private static PageNodeVersionDto Version(
        int versionId, string content = "v", int minor = 0, DateTime? from = null, DateTime? to = null,
        bool current = false, string plain = "") =>
        new()
        {
            Id = Guid.NewGuid(),
            VersionId = versionId,
            MinorVersion = minor,
            IsCurrent = current,
            PublishFrom = from,
            PublishTo = to,
            Content = content,
            BodyPlainText = plain,
            CreatedDate = new DateTime(2026, 1, 1),
            UpdatedDate = new DateTime(2026, 1, 1)
        };

    private static ContentBlockDto Block(int id, string key, string type, string value, Guid contentId = default) =>
        new() { Id = id, ContentId = contentId, Key = key, BlockType = type, Value = value };

    // ── Export ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_SetsCurrentSchemaAndVersion()
    {
        var bundle = await _sut.ExportAsync();
        Assert.Equal(ContentBundle.CurrentSchema, bundle.Schema);
        Assert.Equal(ContentBundle.CurrentSchemaVersion, bundle.SchemaVersion);
    }

    [Fact]
    public async Task ExportAsync_EmptyTree_ReturnsEmptyPageNodes()
    {
        var bundle = await _sut.ExportAsync();
        Assert.Empty(bundle.PageNodes);
        Assert.Empty(bundle.ContentBlocks);
    }

    [Fact]
    public async Task ExportAsync_CarriesGuidIdentityAndParentGuid()
    {
        _pages.GetTreeAsync().Returns(
        [
            Node(GuidA, parentId: null, segment: "parent", title: "Parent", pageType: "folder"),
            Node(GuidB, parentId: GuidA, segment: "child",  title: "Child")
        ]);

        var bundle = await _sut.ExportAsync();

        Assert.Collection(bundle.PageNodes,
            p =>
            {
                Assert.Equal(GuidA, p.Id);
                Assert.Null(p.ParentId);
                Assert.Equal("Parent", p.Title);
                Assert.Equal("folder", p.PageType);
            },
            c =>
            {
                Assert.Equal(GuidB, c.Id);
                Assert.Equal(GuidA, c.ParentId);
                Assert.Equal("Child", c.Title);
            });
    }

    [Fact]
    public async Task ExportAsync_WalksDepthFirst_ChildAfterParent()
    {
        // GuidA is first at root, GuidB is second at root, GuidC is a child of GuidA. Expect the
        // child of GuidA to appear immediately after GuidA and before GuidB.
        _pages.GetTreeAsync().Returns(
        [
            Node(GuidA, parentId: null, segment: "first",  title: "First",  sortOrder: 0),
            Node(GuidB, parentId: null, segment: "second", title: "Second", sortOrder: 1),
            Node(GuidC, parentId: GuidA, segment: "kid",   title: "Kid",    sortOrder: 0)
        ]);

        var bundle = await _sut.ExportAsync();

        Assert.Equal(["First", "Kid", "Second"], bundle.PageNodes.Select(p => p.Title));
    }

    [Fact]
    public async Task ExportAsync_OrdersSiblingsBySortOrder_AndCarriesSortOrder()
    {
        _pages.GetTreeAsync().Returns(
        [
            Node(GuidA, parentId: null, segment: "alpha", title: "Alpha", sortOrder: 2),
            Node(GuidB, parentId: null, segment: "beta",  title: "Beta",  sortOrder: 0),
            Node(GuidC, parentId: null, segment: "gamma", title: "Gamma", sortOrder: 1)
        ]);

        var bundle = await _sut.ExportAsync();

        Assert.Equal(["Beta", "Gamma", "Alpha"], bundle.PageNodes.Select(p => p.Title));
        Assert.Equal([0, 1, 2], bundle.PageNodes.Select(p => p.SortOrder));
    }

    [Fact]
    public async Task ExportAsync_FolderNode_HasNoVersions()
    {
        _pages.GetTreeAsync().Returns([Node(GuidA, null, "folder", "Folder", pageType: "folder")]);

        var bundle = await _sut.ExportAsync();

        Assert.Empty(bundle.PageNodes[0].Versions);
        // Never fetched versions for a folder — folders are unversioned.
        await _pages.DidNotReceive().GetVersionsAsync(GuidA);
    }

    [Fact]
    public async Task ExportAsync_ContentNode_CarriesAllVersionsInAscVersionIdOrder()
    {
        _pages.GetTreeAsync().Returns([Node(GuidA, null, "p", "Page", pageType: "content")]);
        _pages.GetVersionsAsync(GuidA).Returns(
        [
            Version(3, content: "third",  from: new DateTime(2026, 3, 1)),
            Version(1, content: "first",  from: new DateTime(2026, 1, 1)),
            Version(2, content: "second", from: new DateTime(2026, 2, 1))
        ]);

        var bundle = await _sut.ExportAsync();

        Assert.Equal([1, 2, 3], bundle.PageNodes[0].Versions.Select(v => v.VersionId));
        Assert.Equal(["first", "second", "third"], bundle.PageNodes[0].Versions.Select(v => v.Content));
    }

    [Fact]
    public async Task ExportAsync_CarriesVersionPublishWindowAndTextFields()
    {
        var from = new DateTime(2026, 1, 1);
        var to   = new DateTime(2026, 12, 31);
        _pages.GetTreeAsync().Returns([Node(GuidA, null, "p", "P", pageType: "content")]);
        _pages.GetVersionsAsync(GuidA).Returns(
        [
            Version(1, content: "{}", minor: 2, from: from, to: to, plain: "hello world")
        ]);

        var bundle = await _sut.ExportAsync();

        var v = bundle.PageNodes[0].Versions.Single();
        Assert.Equal(2, v.MinorVersion);
        Assert.Equal(from, v.PublishFrom);
        Assert.Equal(to, v.PublishTo);
        Assert.Equal("hello world", v.BodyPlainText);
    }

    [Fact]
    public async Task ExportAsync_ContentBlocks_OrderedByKeyOrdinal()
    {
        _blockRepo.GetAllAsync().Returns(
        [
            Block(1, "footer-legal", "Text", "©", contentId: GuidA),
            Block(2, "app.notice",   "Html", "N", contentId: GuidB),
            Block(3, "banner",       "Text", "B", contentId: GuidC)
        ]);

        var bundle = await _sut.ExportAsync();

        Assert.Equal(["app.notice", "banner", "footer-legal"], bundle.ContentBlocks.Select(b => b.Key));
    }

    // ── Selection ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_Selection_IncludesAncestorsOfSelectedPages()
    {
        // Root A has child B has grandchild C. Selecting only C should include A and B too.
        _pages.GetTreeAsync().Returns(
        [
            Node(GuidA, parentId: null,  segment: "a", title: "A"),
            Node(GuidB, parentId: GuidA, segment: "b", title: "B"),
            Node(GuidC, parentId: GuidB, segment: "c", title: "C"),
            Node(GuidD, parentId: null,  segment: "d", title: "D")   // unrelated, must be excluded
        ]);

        var bundle = await _sut.ExportAsync(new ContentExportSelection(
            new HashSet<Guid> { GuidC },
            new HashSet<Guid>()));

        Assert.Equal(
            new HashSet<Guid> { GuidA, GuidB, GuidC },
            bundle.PageNodes.Select(p => p.Id).ToHashSet());
    }

    [Fact]
    public async Task ExportAsync_Selection_FiltersContentBlocksById()
    {
        _blockRepo.GetAllAsync().Returns(
        [
            Block(1, "keep",  "Text", "k", contentId: GuidA),
            Block(2, "drop",  "Text", "d", contentId: GuidB)
        ]);

        var bundle = await _sut.ExportAsync(new ContentExportSelection(
            new HashSet<Guid>(),
            new HashSet<Guid> { GuidA }));

        Assert.Equal(["keep"], bundle.ContentBlocks.Select(b => b.Key));
    }

    // ── Preview ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_MarksNewVsExisting()
    {
        _pages.GetByIdAsync(GuidA).Returns(new PageNodeDto
        {
            Id = GuidA, Segment = "a", Path = "a", Title = "Old Title", PageType = "content"
        });
        _pages.GetByIdAsync(GuidB).ReturnsNull();

        var bundle = new ContentBundle
        {
            PageNodes =
            [
                new() { Id = GuidA, Title = "New Title", Segment = "a", PageType = "content" },
                new() { Id = GuidB, Title = "Fresh",     Segment = "b", PageType = "content" }
            ]
        };

        var preview = await _sut.PreviewAsync(bundle);

        var a = preview.Pages.Single(p => p.Id == GuidA);
        var b = preview.Pages.Single(p => p.Id == GuidB);
        Assert.True(a.Exists);
        Assert.Contains("Old Title", a.ExistingDescription);
        Assert.False(b.Exists);
    }

    [Fact]
    public async Task PreviewAsync_UnknownParent_MarksParentMissing()
    {
        // Parent GuidA is not in the bundle and not in the target.
        _pages.GetByIdAsync(Arg.Any<Guid>()).ReturnsNull();

        var bundle = new ContentBundle
        {
            PageNodes =
            [
                new() { Id = GuidB, ParentId = GuidA, Title = "Orphan", Segment = "orphan", PageType = "content" }
            ]
        };

        var preview = await _sut.PreviewAsync(bundle);

        var orphan = preview.Pages.Single(p => p.Id == GuidB);
        Assert.True(orphan.ParentMissing);
        Assert.False(orphan.Exists);
    }

    // ── Import ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_NewNode_CallsCreateThenReplaceVersions()
    {
        _pages.GetByIdAsync(GuidA).ReturnsNull();
        _pages.CreateNodeForStagingAsync(
                GuidA, null, "p", "p", "P", "content", 0, null)
            .Returns(new PageNodeDto { Id = GuidA, Segment = "p", Path = "p", Title = "P", PageType = "content" });

        var bundle = new ContentBundle
        {
            PageNodes =
            [
                new()
                {
                    Id = GuidA, Segment = "p", Title = "P", PageType = "content",
                    Versions = [new() { VersionId = 1, Content = "{}" }]
                }
            ]
        };

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Fail);

        Assert.Equal(1, result.PageNodesCreated);
        await _pages.Received(1).CreateNodeForStagingAsync(
            GuidA, null, "p", "p", "P", "content", 0, null);
        await _pages.Received(1).ReplaceAllVersionsForStagingAsync(
            GuidA, Arg.Is<IReadOnlyList<PageNodeVersionDto>>(v => v.Count == 1 && v[0].VersionId == 1), null);
    }

    [Fact]
    public async Task ImportAsync_NewFolder_DoesNotReplaceVersions()
    {
        _pages.GetByIdAsync(GuidA).ReturnsNull();
        _pages.CreateNodeForStagingAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(new PageNodeDto { Id = GuidA, Segment = "f", Path = "f", Title = "F", PageType = "folder" });

        var bundle = new ContentBundle
        {
            PageNodes = [new() { Id = GuidA, Segment = "f", Title = "F", PageType = "folder" }]
        };

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Fail);

        Assert.Equal(1, result.PageNodesCreated);
        await _pages.DidNotReceive().ReplaceAllVersionsForStagingAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<PageNodeVersionDto>>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ImportAsync_Collision_SkipMode_LeavesExistingAlone()
    {
        _pages.GetByIdAsync(GuidA).Returns(new PageNodeDto
        {
            Id = GuidA, Segment = "p", Path = "p", Title = "Old", PageType = "content"
        });

        var bundle = new ContentBundle
        {
            PageNodes = [new() { Id = GuidA, Segment = "p", Title = "New", PageType = "content" }]
        };

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Skip);

        Assert.Equal(1, result.PageNodesSkipped);
        Assert.Equal(0, result.PageNodesUpdated);
        await _pages.DidNotReceive().UpdateNodeForStagingAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ImportAsync_Collision_ReplaceMode_UpdatesNodeAndReplacesVersions()
    {
        _pages.GetByIdAsync(GuidA).Returns(new PageNodeDto
        {
            Id = GuidA, Segment = "p", Path = "p", Title = "Old", PageType = "content"
        });

        var bundle = new ContentBundle
        {
            PageNodes =
            [
                new()
                {
                    Id = GuidA, Segment = "p", Title = "New", PageType = "content",
                    Versions = [new() { VersionId = 1, Content = "{}" }]
                }
            ]
        };

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Replace);

        Assert.Equal(1, result.PageNodesUpdated);
        await _pages.Received(1).UpdateNodeForStagingAsync(GuidA, "p", "p", "New", 0, null);
        await _pages.Received(1).ReplaceAllVersionsForStagingAsync(
            GuidA, Arg.Any<IReadOnlyList<PageNodeVersionDto>>(), null);
    }

    [Fact]
    public async Task ImportAsync_Collision_FailMode_ThrowsBeforeAnyWrite()
    {
        _pages.GetByIdAsync(GuidA).Returns(new PageNodeDto
        {
            Id = GuidA, Segment = "p", Path = "p", Title = "Old", PageType = "content"
        });

        var bundle = new ContentBundle
        {
            PageNodes = [new() { Id = GuidA, Segment = "p", Title = "New", PageType = "content" }]
        };

        await Assert.ThrowsAsync<ContentImportConflictException>(
            () => _sut.ImportAsync(bundle, ContentImportMode.Fail));

        await _pages.DidNotReceive().CreateNodeForStagingAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>());
        await _pages.DidNotReceive().UpdateNodeForStagingAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ImportAsync_PerItemDecisionOverridesGlobalMode()
    {
        // Global mode is Fail, but the per-item decision resolves the collision as Skip.
        _pages.GetByIdAsync(GuidA).Returns(new PageNodeDto
        {
            Id = GuidA, Segment = "p", Path = "p", Title = "Old", PageType = "content"
        });

        var bundle = new ContentBundle
        {
            PageNodes = [new() { Id = GuidA, Segment = "p", Title = "New", PageType = "content" }]
        };

        var decisions = new Dictionary<Guid, ContentImportMode> { [GuidA] = ContentImportMode.Skip };

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Fail, decisions);

        Assert.Equal(1, result.PageNodesSkipped);
    }

    [Fact]
    public async Task ImportAsync_ParentInBundle_IsCreatedBeforeChild()
    {
        // Bundle is deliberately child-first order; the service must re-order so the parent is
        // created first (and its path is threaded through to build the child's path).
        _pages.GetByIdAsync(Arg.Any<Guid>()).ReturnsNull();
        _pages.CreateNodeForStagingAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(ci => new PageNodeDto
            {
                Id = (Guid)ci[0],
                ParentId = (Guid?)ci[1],
                Segment = (string)ci[2],
                Path = (string)ci[3],
                Title = (string)ci[4],
                PageType = (string)ci[5]
            });

        var bundle = new ContentBundle
        {
            PageNodes =
            [
                new() { Id = GuidB, ParentId = GuidA, Segment = "child",  Title = "Child",  PageType = "content" },
                new() { Id = GuidA, ParentId = null,  Segment = "parent", Title = "Parent", PageType = "folder" }
            ]
        };

        await _sut.ImportAsync(bundle, ContentImportMode.Fail);

        // Received.InOrder-style check: verify the child's create call carried the parent's Id.
        await _pages.Received(1).CreateNodeForStagingAsync(
            GuidA, null, "parent", "parent", "Parent", "folder", 0, null);
        await _pages.Received(1).CreateNodeForStagingAsync(
            GuidB, GuidA, "child", "parent/child", "Child", "content", 0, null);
    }

    [Fact]
    public async Task ImportAsync_UnknownParent_IsReportedAsError_NoOrphanCreated()
    {
        // Parent GuidA is neither in the bundle nor in the target.
        _pages.GetByIdAsync(Arg.Any<Guid>()).ReturnsNull();

        var bundle = new ContentBundle
        {
            PageNodes =
            [
                new() { Id = GuidB, ParentId = GuidA, Segment = "orphan", Title = "Orphan", PageType = "content" }
            ]
        };

        var result = await _sut.ImportAsync(bundle, ContentImportMode.Fail);

        Assert.Single(result.Errors);
        Assert.Contains("Orphan", result.Errors[0]);
        Assert.Equal(0, result.PageNodesCreated);
        await _pages.DidNotReceive().CreateNodeForStagingAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>());
    }
}
