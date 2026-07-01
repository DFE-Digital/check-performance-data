using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

// Service orchestrates: path computation on create, version seeding, working-draft resolution,
// publish scheduling, and soft-delete with children guard.
// Uses a hand-written in-memory fake (mirrors ContentPageEditorTests' FakeContentPageRepository).
public class PageNodeServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakePageNodeRepository _repo = new();
    private PageNodeService Sut() => new(_repo);

    // ── CreatePageAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task Create_AtRoot_Path_EqualsSegment()
    {
        var node = await Sut().CreatePageAsync(null, "guidance", "Guidance", "folder", "u1");

        Assert.Equal("guidance", node.Path);
        Assert.Equal("guidance", node.Segment);
        Assert.Null(node.ParentId);
    }

    [Fact]
    public async Task Create_WithParent_Path_IsParentPlusSlashPlusSegment()
    {
        var parent = await Sut().CreatePageAsync(null, "guidance", "Guidance", "folder", "u1");

        var child = await Sut().CreatePageAsync(parent.Id, "ks4", "KS4", "content", "u1");

        Assert.Equal("guidance/ks4", child.Path);
        Assert.Equal(parent.Id, child.ParentId);
    }

    [Fact]
    public async Task Create_ContentType_Seeds_Empty_Draft_Version()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");

        var versions = await _repo.GetVersionsAsync(node.Id);
        Assert.Single(versions);
        Assert.Equal("[]", versions[0].Content);
        Assert.Null(versions[0].PublishFrom);  // seeded as a draft, not scheduled
    }

    [Fact]
    public async Task Create_WikiType_Seeds_Empty_Draft_Version_With_EmptyString_Content()
    {
        var node = await Sut().CreatePageAsync(null, "wiki", "Wiki", "wiki", "u1");

        var versions = await _repo.GetVersionsAsync(node.Id);
        Assert.Single(versions);
        Assert.Equal(string.Empty, versions[0].Content);
        Assert.Null(versions[0].PublishFrom);
    }

    [Fact]
    public async Task Create_FolderType_Seeds_No_Version()
    {
        var node = await Sut().CreatePageAsync(null, "folder", "Folder", "folder", "u1");

        var versions = await _repo.GetVersionsAsync(node.Id);
        Assert.Empty(versions);
    }

    [Fact]
    public async Task Create_WithUnknownParent_Throws()
    {
        var badParent = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sut().CreatePageAsync(badParent, "child", "Child", "content", "u1"));
    }

    // ── GetTreeAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTree_Returns_All_Nodes()
    {
        await Sut().CreatePageAsync(null, "a", "A", "folder", null);
        await Sut().CreatePageAsync(null, "b", "B", "folder", null);

        var tree = await Sut().GetTreeAsync();

        Assert.Equal(2, tree.Count);
    }

    // ── GetLivePageAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetLivePage_Returns_Null_When_Path_Not_Found()
    {
        var result = await Sut().GetLivePageAsync("nonexistent", Now);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLivePage_Returns_Null_When_No_Live_Version()
    {
        // Node exists but its only version is an unscheduled draft
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");

        var result = await Sut().GetLivePageAsync("page", Now);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLivePage_Returns_Node_And_Version_When_Live()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        // Manually schedule the draft version
        var versions = await _repo.GetVersionsAsync(node.Id);
        await _repo.UpdateVersionWindowAsync(node.Id, versions[0].VersionId, Now.AddDays(-1), null, null);

        var result = await Sut().GetLivePageAsync("page", Now);

        Assert.NotNull(result);
        Assert.Equal(node.Id, result!.Node.Id);
        Assert.Equal("[]", result.Version.Content);
    }

    // ── SaveWorkingContentAsync ──────────────────────────────────────────────

    [Fact]
    public async Task SaveWorking_Updates_Existing_Draft_In_Place()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        var before = await _repo.GetVersionsAsync(node.Id);
        Assert.Single(before);

        await Sut().SaveWorkingContentAsync(node.Id, "[{\"updated\":true}]", "updated", "u1");

        var after = await _repo.GetVersionsAsync(node.Id);
        Assert.Single(after);   // still one version — no new version created
        Assert.Equal("[{\"updated\":true}]", after[0].Content);
    }

    [Fact]
    public async Task SaveWorking_Creates_New_Draft_When_All_Versions_Are_Scheduled()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        var versions = await _repo.GetVersionsAsync(node.Id);
        // Schedule the seeded draft so it's no longer "working"
        await _repo.UpdateVersionWindowAsync(node.Id, versions[0].VersionId, Now.AddDays(-1), null, null);

        await Sut().SaveWorkingContentAsync(node.Id, "[{\"new\":true}]", "new content", "u1");

        var all = await _repo.GetVersionsAsync(node.Id);
        Assert.Equal(2, all.Count);
        // Highest VersionId (2) should be the new unscheduled draft
        var newest = all.First();   // ordered descending
        Assert.Equal("[{\"new\":true}]", newest.Content);
        Assert.Null(newest.PublishFrom);
    }

    // ── PublishAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_Sets_Window_On_Existing_Version()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        var versionId = (await _repo.GetVersionsAsync(node.Id))[0].VersionId;

        await Sut().PublishAsync(node.Id, versionId, Now.AddDays(-1), null, null);

        var versions = await _repo.GetVersionsAsync(node.Id);
        Assert.Equal(Now.AddDays(-1), versions[0].PublishFrom);
        Assert.Null(versions[0].PublishTo);
    }

    [Fact]
    public async Task Publish_Recomputes_IsCurrent()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        var versionId = (await _repo.GetVersionsAsync(node.Id))[0].VersionId;
        // Before publish: draft has no window → resolver picks nothing → IsCurrent = false
        Assert.False(_repo.GetVersionsSync(node.Id)[0].IsCurrent);

        await Sut().PublishAsync(node.Id, versionId, Now.AddDays(-1), null, null);

        Assert.True(_repo.GetVersionsSync(node.Id)[0].IsCurrent);
    }

    [Fact]
    public async Task Publish_Threads_UserId_To_Repository()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        var versionId = (await _repo.GetVersionsAsync(node.Id))[0].VersionId;

        await Sut().PublishAsync(node.Id, versionId, Now.AddDays(-1), null, "publisher-1");

        Assert.Equal("publisher-1", _repo.LastWindowUserId);
    }

    // ── GetVersionsAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetVersions_Returns_All_Versions_In_Descending_Order()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        // Save working content twice to create a second version scenario
        var v = (await _repo.GetVersionsAsync(node.Id))[0];
        await _repo.UpdateVersionWindowAsync(node.Id, v.VersionId, Now.AddDays(-5), null, null);
        await Sut().SaveWorkingContentAsync(node.Id, "[2]", "", null);

        var versions = await Sut().GetVersionsAsync(node.Id);

        Assert.Equal(2, versions.Count);
        Assert.True(versions[0].VersionId > versions[1].VersionId); // descending
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Refuses_When_Node_Has_Children()
    {
        var parent = await Sut().CreatePageAsync(null, "parent", "Parent", "folder", null);
        await Sut().CreatePageAsync(parent.Id, "child", "Child", "content", null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sut().DeleteAsync(parent.Id, "u1"));
    }

    [Fact]
    public async Task Delete_SoftDeletes_When_No_Children()
    {
        var node = await Sut().CreatePageAsync(null, "leaf", "Leaf", "content", null);

        await Sut().DeleteAsync(node.Id, "u1");

        Assert.True(_repo.IsDeleted(node.Id));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // In-memory fake repository
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed class FakePageNodeRepository : IPageNodeRepository
    {
        private readonly List<FakeNode> _nodes = [];
        private readonly List<FakeVersion> _versions = [];

        // Synchronous accessor for assertion convenience (avoids async in test body)
        public List<PageNodeVersionDto> GetVersionsSync(Guid nodeId) =>
            _versions
                .Where(v => v.NodeId == nodeId)
                .OrderByDescending(v => v.VersionId)
                .Select(ToVersionDto)
                .ToList();

        public bool IsDeleted(Guid nodeId) =>
            _nodes.Any(n => n.Id == nodeId && n.IsDeleted);

        /// <summary>Records the userId passed to the most recent UpdateVersionWindowAsync call.</summary>
        public string? LastWindowUserId { get; private set; }

        // ── IPageNodeRepository ──────────────────────────────────────────────

        public Task<List<PageNodeTreeItemDto>> GetTreeAsync() =>
            Task.FromResult(
                _nodes
                    .Where(n => !n.IsDeleted)
                    .Select(n => new PageNodeTreeItemDto
                    {
                        Id = n.Id,
                        ParentId = n.ParentId,
                        Segment = n.Segment,
                        Path = n.Path,
                        SortOrder = 0,
                        Title = n.Title,
                        PageType = n.PageType,
                        HasLiveVersion = _versions.Any(v => v.NodeId == n.Id && v.IsCurrent)
                    })
                    .ToList());

        public Task<PageNodeDto?> GetByPathAsync(string path) =>
            Task.FromResult(
                _nodes
                    .Where(n => n.Path == path && !n.IsDeleted)
                    .Select(ToNodeDto)
                    .FirstOrDefault());

        public Task<PageNodeDto?> GetByIdAsync(Guid id) =>
            Task.FromResult(
                _nodes
                    .Where(n => n.Id == id && !n.IsDeleted)
                    .Select(ToNodeDto)
                    .FirstOrDefault());

        public Task<PageNodeDto> CreateNodeAsync(
            Guid? parentId, string segment, string path, string title, string pageType, string? userId)
        {
            var node = new FakeNode
            {
                Id = Guid.NewGuid(),
                ParentId = parentId,
                Segment = segment,
                Path = path,
                Title = title,
                PageType = pageType
            };
            _nodes.Add(node);
            return Task.FromResult(ToNodeDto(node));
        }

        public Task<int> AddVersionAsync(
            Guid nodeId, string content, string bodyPlainText,
            DateTime? publishFrom, DateTime? publishTo, string? userId)
        {
            var max = _versions.Where(v => v.NodeId == nodeId).MaxOrDefault(v => v.VersionId);
            var versionId = max + 1;

            _versions.Add(new FakeVersion
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                VersionId = versionId,
                Content = content,
                PublishFrom = publishFrom,
                PublishTo = publishTo,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            });

            RecomputeCurrentSync(nodeId, DateTime.UtcNow);
            return Task.FromResult(versionId);
        }

        public Task UpdateVersionContentAsync(
            Guid nodeId, int versionId, string content, string bodyPlainText, string? userId)
        {
            var v = _versions.First(v => v.NodeId == nodeId && v.VersionId == versionId);
            v.Content = content;
            v.UpdatedDate = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        public Task UpdateVersionWindowAsync(
            Guid nodeId, int versionId, DateTime? publishFrom, DateTime? publishTo, string? userId)
        {
            var v = _versions.First(v => v.NodeId == nodeId && v.VersionId == versionId);
            v.PublishFrom = publishFrom;
            v.PublishTo = publishTo;
            v.UpdatedDate = DateTime.UtcNow;
            LastWindowUserId = userId;
            return Task.CompletedTask;
        }

        public Task<List<PageNodeVersionDto>> GetVersionsAsync(Guid nodeId) =>
            Task.FromResult(
                _versions
                    .Where(v => v.NodeId == nodeId)
                    .OrderByDescending(v => v.VersionId)
                    .Select(ToVersionDto)
                    .ToList());

        public Task<PageNodeVersionDto?> GetLiveVersionAsync(Guid nodeId, DateTime nowUtc)
        {
            var windows = _versions
                .Where(v => v.NodeId == nodeId)
                .Select(v => new PageVersionWindow(v.VersionId, v.PublishFrom, v.PublishTo));

            var liveId = LiveVersionResolver.Resolve(windows, nowUtc);
            if (liveId is null)
                return Task.FromResult<PageNodeVersionDto?>(null);

            var live = _versions
                .Where(v => v.NodeId == nodeId && v.VersionId == liveId)
                .Select(ToVersionDto)
                .FirstOrDefault();
            return Task.FromResult(live);
        }

        public Task RecomputeCurrentAsync(Guid nodeId, DateTime nowUtc)
        {
            RecomputeCurrentSync(nodeId, nowUtc);
            return Task.CompletedTask;
        }

        public Task<bool> HasChildrenAsync(Guid nodeId) =>
            Task.FromResult(_nodes.Any(n => n.ParentId == nodeId && !n.IsDeleted));

        public Task SoftDeleteAsync(Guid nodeId, string? userId)
        {
            var node = _nodes.First(n => n.Id == nodeId);
            node.IsDeleted = true;
            return Task.CompletedTask;
        }

        public Task ExecuteInTransactionAsync(Func<Task> work) => work();

        // ── helpers ──────────────────────────────────────────────────────────

        private void RecomputeCurrentSync(Guid nodeId, DateTime nowUtc)
        {
            var windows = _versions
                .Where(v => v.NodeId == nodeId)
                .Select(v => new PageVersionWindow(v.VersionId, v.PublishFrom, v.PublishTo));

            var liveId = LiveVersionResolver.Resolve(windows, nowUtc);
            foreach (var v in _versions.Where(v => v.NodeId == nodeId))
                v.IsCurrent = v.VersionId == liveId;
        }

        private static PageNodeDto ToNodeDto(FakeNode n) => new()
        {
            Id = n.Id,
            ParentId = n.ParentId,
            Segment = n.Segment,
            Path = n.Path,
            SortOrder = 0,
            Title = n.Title,
            PageType = n.PageType
        };

        private static PageNodeVersionDto ToVersionDto(FakeVersion v) => new()
        {
            Id = v.Id,
            VersionId = v.VersionId,
            IsCurrent = v.IsCurrent,
            PublishFrom = v.PublishFrom,
            PublishTo = v.PublishTo,
            Content = v.Content,
            CreatedDate = v.CreatedDate,
            CreatedBy = null,
            UpdatedDate = v.UpdatedDate
        };

        private sealed class FakeNode
        {
            public Guid Id { get; init; }
            public Guid? ParentId { get; init; }
            public required string Segment { get; init; }
            public required string Path { get; init; }
            public required string Title { get; init; }
            public required string PageType { get; init; }
            public bool IsDeleted { get; set; }
        }

        private sealed class FakeVersion
        {
            public Guid Id { get; init; }
            public Guid NodeId { get; init; }
            public int VersionId { get; init; }
            public required string Content { get; set; }
            public DateTime? PublishFrom { get; set; }
            public DateTime? PublishTo { get; set; }
            public bool IsCurrent { get; set; }
            public DateTime CreatedDate { get; init; }
            public DateTime UpdatedDate { get; set; }
        }
    }
}

// Local extension used only in this test file.
file static class EnumerableExtensions
{
    public static int MaxOrDefault<T>(this IEnumerable<T> source, Func<T, int> selector) =>
        source.Any() ? source.Max(selector) : 0;
}
