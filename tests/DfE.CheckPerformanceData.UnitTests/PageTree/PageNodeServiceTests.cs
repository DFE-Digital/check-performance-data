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

    // ── GetNodeByIdAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetNodeById_Returns_Null_When_Not_Found()
    {
        var result = await Sut().GetNodeByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetNodeById_Returns_Node_When_Found()
    {
        var node = await Sut().CreatePageAsync(null, "guidance", "Guidance", "folder", "u1");

        var result = await Sut().GetNodeByIdAsync(node.Id);

        Assert.NotNull(result);
        Assert.Equal(node.Id, result!.Id);
        Assert.Equal("guidance", result.Path);
        Assert.Equal("folder", result.PageType);
    }

    [Fact]
    public async Task GetNodeById_Returns_Null_After_SoftDelete()
    {
        var node = await Sut().CreatePageAsync(null, "leaf", "Leaf", "content", null);
        await Sut().DeleteAsync(node.Id, "u1");

        var result = await Sut().GetNodeByIdAsync(node.Id);

        Assert.Null(result);
    }

    // ── GetNodeByPathAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetNodeByPath_Returns_Null_When_Path_Not_Found()
    {
        var result = await Sut().GetNodeByPathAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetNodeByPath_Returns_Node_When_Path_Exists()
    {
        var node = await Sut().CreatePageAsync(null, "guidance", "Guidance", "folder", "u1");

        var result = await Sut().GetNodeByPathAsync("guidance");

        Assert.NotNull(result);
        Assert.Equal(node.Id, result!.Id);
        Assert.Equal("guidance", result.Path);
        Assert.Equal("folder", result.PageType);
    }

    [Fact]
    public async Task GetNodeByPath_Returns_Folder_Node_Without_Live_Version()
    {
        // Folder nodes are never versioned; GetNodeByPathAsync must still return them.
        var node = await Sut().CreatePageAsync(null, "support", "Support", "folder", "u1");
        var versions = await _repo.GetVersionsAsync(node.Id);
        Assert.Empty(versions); // confirm no version was seeded

        var result = await Sut().GetNodeByPathAsync("support");

        Assert.NotNull(result);
        Assert.Equal("folder", result!.PageType);
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

    // ── GetWorkingOrLatestContentAsync ───────────────────────────────────────

    [Fact]
    public async Task GetWorkingOrLatest_WithDraft_ReturnsDraftContent()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        await Sut().SaveWorkingContentAsync(node.Id, "[{\"draft\":true}]", "", null);

        var content = await Sut().GetWorkingOrLatestContentAsync(node.Id);

        Assert.Equal("[{\"draft\":true}]", content);
    }

    [Fact]
    public async Task GetWorkingOrLatest_NoVersions_ReturnsNull()
    {
        // Folder nodes are never versioned.
        var node = await Sut().CreatePageAsync(null, "section", "Section", "folder", "u1");

        var content = await Sut().GetWorkingOrLatestContentAsync(node.Id);

        Assert.Null(content);
    }

    [Fact]
    public async Task GetWorkingOrLatest_NoDraft_ReturnsLatestPublishedContent()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        var versions = await _repo.GetVersionsAsync(node.Id);
        // Publish the seeded draft so no draft remains.
        await _repo.UpdateVersionWindowAsync(node.Id, versions[0].VersionId, Now.AddDays(-1), null, null);

        var content = await Sut().GetWorkingOrLatestContentAsync(node.Id);

        Assert.Equal("[]", content); // seeded empty content tree
    }

    [Fact]
    public async Task GetWorkingOrLatest_DraftAndPublished_ReturnsDraftNotPublished()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        var versions = await _repo.GetVersionsAsync(node.Id);
        // Publish the seeded draft.
        await _repo.UpdateVersionWindowAsync(node.Id, versions[0].VersionId, Now.AddDays(-2), null, null);
        // Create a new working draft.
        await Sut().SaveWorkingContentAsync(node.Id, "[{\"draft\":true}]", "", null);

        var content = await Sut().GetWorkingOrLatestContentAsync(node.Id);

        // Draft wins over the published version.
        Assert.Equal("[{\"draft\":true}]", content);
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

    // ── PublishDraftAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task PublishDraft_WithDraft_PublishesDraft_And_SetsCurrent()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        // Seeded version is an unscheduled draft.
        Assert.False(_repo.GetVersionsSync(node.Id)[0].IsCurrent);

        await Sut().PublishDraftAsync(node.Id, "publisher");

        var versions = _repo.GetVersionsSync(node.Id);
        Assert.NotNull(versions[0].PublishFrom);
        Assert.True(versions[0].IsCurrent);
    }

    [Fact]
    public async Task PublishDraft_ThreadsUserIdToRepository()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");

        await Sut().PublishDraftAsync(node.Id, "pub-user");

        Assert.Equal("pub-user", _repo.LastWindowUserId);
    }

    [Fact]
    public async Task PublishDraft_WhenNoDraftExists_IsNoOp()
    {
        // A folder node has no versions — PublishDraftAsync must not throw.
        var node = await Sut().CreatePageAsync(null, "folder", "Folder", "folder", "u1");
        var before = _repo.GetVersionsSync(node.Id);
        Assert.Empty(before);

        // Should not throw.
        await Sut().PublishDraftAsync(node.Id, "u1");

        Assert.Empty(_repo.GetVersionsSync(node.Id));
    }

    [Fact]
    public async Task PublishDraft_WhenLatestVersionScheduledFuture_PublishesItNow()
    {
        // Repro of the "Publish draft does nothing" bug: a page whose only version is scheduled for
        // the future has no null-window draft and is not yet live. "Publish draft" must publish the
        // version being edited (the latest) live NOW, overriding the future schedule.
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        var versions = await _repo.GetVersionsAsync(node.Id);
        await _repo.UpdateVersionWindowAsync(node.Id, versions[0].VersionId, Now.AddDays(10), null, null);

        await Sut().PublishDraftAsync(node.Id, "u1");

        var after = _repo.GetVersionsSync(node.Id);
        Assert.True(after[0].IsCurrent);                        // now live
        Assert.NotEqual(Now.AddDays(10), after[0].PublishFrom); // future schedule overridden to now
    }

    // ── UnpublishAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task Unpublish_ClearsPublishWindow_And_MakesVersionNotCurrent()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        var versionId = (await _repo.GetVersionsAsync(node.Id))[0].VersionId;
        // Make the version live.
        await _repo.UpdateVersionWindowAsync(node.Id, versionId, Now.AddDays(-1), null, null);
        await _repo.RecomputeCurrentAsync(node.Id, Now);
        Assert.True(_repo.GetVersionsSync(node.Id)[0].IsCurrent);

        await Sut().UnpublishAsync(node.Id, "u1");

        var versions = _repo.GetVersionsSync(node.Id);
        Assert.Null(versions[0].PublishFrom);
        Assert.False(versions[0].IsCurrent);
    }

    [Fact]
    public async Task Unpublish_ThreadsUserIdToRepository()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        var versionId = (await _repo.GetVersionsAsync(node.Id))[0].VersionId;
        await _repo.UpdateVersionWindowAsync(node.Id, versionId, Now.AddDays(-1), null, null);
        await _repo.RecomputeCurrentAsync(node.Id, Now);

        await Sut().UnpublishAsync(node.Id, "unpub-user");

        Assert.Equal("unpub-user", _repo.LastWindowUserId);
    }

    [Fact]
    public async Task Unpublish_WhenNoVersionIsLive_IsNoOp()
    {
        // Draft page (never published) — should not throw.
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        Assert.False(_repo.GetVersionsSync(node.Id)[0].IsCurrent);

        await Sut().UnpublishAsync(node.Id, "u1");

        // Draft version must be unchanged.
        var after = _repo.GetVersionsSync(node.Id);
        Assert.Null(after[0].PublishFrom);
        Assert.False(after[0].IsCurrent);
    }

    // ── IsPublishedAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task IsPublished_ReturnsFalse_WhenDraftOnly()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");

        var result = await Sut().IsPublishedAsync(node.Id);

        Assert.False(result);
    }

    [Fact]
    public async Task IsPublished_ReturnsTrue_WhenVersionIsLive()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        var versionId = (await _repo.GetVersionsAsync(node.Id))[0].VersionId;
        await _repo.UpdateVersionWindowAsync(node.Id, versionId, Now.AddDays(-1), null, null);
        await _repo.RecomputeCurrentAsync(node.Id, Now);

        var result = await Sut().IsPublishedAsync(node.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task IsPublished_ReturnsFalse_AfterUnpublish()
    {
        var node = await Sut().CreatePageAsync(null, "page", "Page", "content", "u1");
        var versionId = (await _repo.GetVersionsAsync(node.Id))[0].VersionId;
        await _repo.UpdateVersionWindowAsync(node.Id, versionId, Now.AddDays(-1), null, null);
        await _repo.RecomputeCurrentAsync(node.Id, Now);
        Assert.True(await Sut().IsPublishedAsync(node.Id)); // sanity

        await Sut().UnpublishAsync(node.Id, "u1");

        Assert.False(await Sut().IsPublishedAsync(node.Id));
    }

    // ── MoveAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Move_Up_PlacesNode_BeforePreviousSibling()
    {
        var parent = await Sut().CreatePageAsync(null, "parent", "Parent", "folder", null);
        var childA = await Sut().CreatePageAsync(parent.Id, "a", "A", "content", null);
        var childB = await Sut().CreatePageAsync(parent.Id, "b", "B", "content", null);

        // Sanity: A comes before B initially (lower sort order, created first).
        var tree0 = await _repo.GetTreeAsync();
        var s0 = tree0.Where(n => n.ParentId == parent.Id)
            .OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedDate).ToList();
        Assert.Equal(childA.Id, s0[0].Id);
        Assert.Equal(childB.Id, s0[1].Id);

        await Sut().MoveAsync(childB.Id, "up");

        // After moving B up, B should precede A.
        var tree1 = await _repo.GetTreeAsync();
        var s1 = tree1.Where(n => n.ParentId == parent.Id)
            .OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedDate).ToList();
        Assert.Equal(childB.Id, s1[0].Id);
        Assert.Equal(childA.Id, s1[1].Id);
    }

    [Fact]
    public async Task Move_Down_PlacesNode_AfterNextSibling()
    {
        var parent = await Sut().CreatePageAsync(null, "parent", "Parent", "folder", null);
        var childA = await Sut().CreatePageAsync(parent.Id, "a", "A", "content", null);
        var childB = await Sut().CreatePageAsync(parent.Id, "b", "B", "content", null);

        await Sut().MoveAsync(childA.Id, "down");

        // After moving A down, B should precede A.
        var tree = await _repo.GetTreeAsync();
        var siblings = tree.Where(n => n.ParentId == parent.Id)
            .OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedDate).ToList();
        Assert.Equal(childB.Id, siblings[0].Id);
        Assert.Equal(childA.Id, siblings[1].Id);
    }

    [Fact]
    public async Task Move_Up_On_First_Sibling_IsNoOp()
    {
        var parent = await Sut().CreatePageAsync(null, "parent", "Parent", "folder", null);
        var childA = await Sut().CreatePageAsync(parent.Id, "a", "A", "content", null);
        var childB = await Sut().CreatePageAsync(parent.Id, "b", "B", "content", null);

        await Sut().MoveAsync(childA.Id, "up"); // already first — should be a no-op

        // A must still precede B.
        var tree = await _repo.GetTreeAsync();
        var siblings = tree.Where(n => n.ParentId == parent.Id)
            .OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedDate).ToList();
        Assert.Equal(childA.Id, siblings[0].Id);
        Assert.Equal(childB.Id, siblings[1].Id);
    }

    [Fact]
    public async Task Move_Down_On_Last_Sibling_IsNoOp()
    {
        var parent = await Sut().CreatePageAsync(null, "parent", "Parent", "folder", null);
        var childA = await Sut().CreatePageAsync(parent.Id, "a", "A", "content", null);
        var childB = await Sut().CreatePageAsync(parent.Id, "b", "B", "content", null);

        await Sut().MoveAsync(childB.Id, "down"); // already last — should be a no-op

        // A must still precede B.
        var tree = await _repo.GetTreeAsync();
        var siblings = tree.Where(n => n.ParentId == parent.Id)
            .OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedDate).ToList();
        Assert.Equal(childA.Id, siblings[0].Id);
        Assert.Equal(childB.Id, siblings[1].Id);
    }

    // ── MoveAsync with equal SortOrders (legacy / seeded data) ──────────────

    [Fact]
    public async Task Move_EqualOrders_Up_MiddleToFirst()
    {
        var parent = await Sut().CreatePageAsync(null, "parent", "Parent", "folder", null);
        var childA = await Sut().CreatePageAsync(parent.Id, "a", "A", "content", null);
        var childB = await Sut().CreatePageAsync(parent.Id, "b", "B", "content", null);
        var childC = await Sut().CreatePageAsync(parent.Id, "c", "C", "content", null);

        // Simulate legacy data where all siblings share SortOrder=0.
        _repo.SetSortOrder(childA.Id, 0);
        _repo.SetSortOrder(childB.Id, 0);
        _repo.SetSortOrder(childC.Id, 0);

        // Initial visible order by (SortOrder=0, CreatedDate): A, B, C.
        // Move B (index 1) up → expected order: B, A, C.
        await Sut().MoveAsync(childB.Id, "up");

        var tree = await _repo.GetTreeAsync();
        var siblings = tree.Where(n => n.ParentId == parent.Id)
            .OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedDate).ToList();
        Assert.Equal(childB.Id, siblings[0].Id);
        Assert.Equal(childA.Id, siblings[1].Id);
        Assert.Equal(childC.Id, siblings[2].Id);
    }

    [Fact]
    public async Task Move_EqualOrders_FirstUp_IsNoOp()
    {
        var parent = await Sut().CreatePageAsync(null, "parent", "Parent", "folder", null);
        var childA = await Sut().CreatePageAsync(parent.Id, "a", "A", "content", null);
        var childB = await Sut().CreatePageAsync(parent.Id, "b", "B", "content", null);
        var childC = await Sut().CreatePageAsync(parent.Id, "c", "C", "content", null);

        _repo.SetSortOrder(childA.Id, 0);
        _repo.SetSortOrder(childB.Id, 0);
        _repo.SetSortOrder(childC.Id, 0);

        // A is first — moving it up is a no-op, order stays A, B, C.
        await Sut().MoveAsync(childA.Id, "up");

        var tree = await _repo.GetTreeAsync();
        var siblings = tree.Where(n => n.ParentId == parent.Id)
            .OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedDate).ToList();
        Assert.Equal(childA.Id, siblings[0].Id);
        Assert.Equal(childB.Id, siblings[1].Id);
        Assert.Equal(childC.Id, siblings[2].Id);
    }

    [Fact]
    public async Task Move_EqualOrders_LastDown_IsNoOp()
    {
        var parent = await Sut().CreatePageAsync(null, "parent", "Parent", "folder", null);
        var childA = await Sut().CreatePageAsync(parent.Id, "a", "A", "content", null);
        var childB = await Sut().CreatePageAsync(parent.Id, "b", "B", "content", null);
        var childC = await Sut().CreatePageAsync(parent.Id, "c", "C", "content", null);

        _repo.SetSortOrder(childA.Id, 0);
        _repo.SetSortOrder(childB.Id, 0);
        _repo.SetSortOrder(childC.Id, 0);

        // C is last — moving it down is a no-op, order stays A, B, C.
        await Sut().MoveAsync(childC.Id, "down");

        var tree = await _repo.GetTreeAsync();
        var siblings = tree.Where(n => n.ParentId == parent.Id)
            .OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedDate).ToList();
        Assert.Equal(childA.Id, siblings[0].Id);
        Assert.Equal(childB.Id, siblings[1].Id);
        Assert.Equal(childC.Id, siblings[2].Id);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // In-memory fake repository
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed class FakePageNodeRepository : IPageNodeRepository
    {
        private readonly List<FakeNode> _nodes = [];
        private readonly List<FakeVersion> _versions = [];
        private int _nextSortOrder;
        private int _creationSeq; // monotonically increasing; feeds CreatedDate for stable ordering

        // Synchronous accessor for assertion convenience (avoids async in test body)
        public List<PageNodeVersionDto> GetVersionsSync(Guid nodeId) =>
            _versions
                .Where(v => v.NodeId == nodeId)
                .OrderByDescending(v => v.VersionId)
                .Select(ToVersionDto)
                .ToList();

        public bool IsDeleted(Guid nodeId) =>
            _nodes.Any(n => n.Id == nodeId && n.IsDeleted);

        public int GetSortOrder(Guid nodeId) =>
            _nodes.First(n => n.Id == nodeId).SortOrder;

        /// <summary>Directly overrides SortOrder for testing equal-order scenarios.</summary>
        public void SetSortOrder(Guid nodeId, int sortOrder) =>
            _nodes.First(n => n.Id == nodeId).SortOrder = sortOrder;

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
                        SortOrder = n.SortOrder,
                        CreatedDate = n.CreatedDate,
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
            var seq = _creationSeq++;
            var node = new FakeNode
            {
                Id = Guid.NewGuid(),
                ParentId = parentId,
                Segment = segment,
                Path = path,
                Title = title,
                PageType = pageType,
                SortOrder = _nextSortOrder++,
                CreatedDate = DateTime.UtcNow.AddSeconds(seq) // monotonically increasing
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
                MinorVersion = 1,           // drafts always start at 1
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
            v.MinorVersion += 1;            // each save bumps the minor
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
            if (publishFrom is not null)
                v.MinorVersion = 0;         // publishing zeros the minor → integer version
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

        public Task SwapSortOrderAsync(Guid nodeId, Guid otherNodeId)
        {
            var nodeA = _nodes.First(n => n.Id == nodeId);
            var nodeB = _nodes.First(n => n.Id == otherNodeId);
            (nodeA.SortOrder, nodeB.SortOrder) = (nodeB.SortOrder, nodeA.SortOrder);
            return Task.CompletedTask;
        }

        public Task SetSiblingOrderAsync(IReadOnlyList<(Guid Id, int SortOrder)> orders)
        {
            foreach (var (id, sortOrder) in orders)
                _nodes.First(n => n.Id == id).SortOrder = sortOrder;
            return Task.CompletedTask;
        }

        public Task ExecuteInTransactionAsync(Func<Task> work) => work();

        // Staging methods are exercised by ContentStagingServiceTests against its own mock; this
        // fake is only used by PageNodeService tests so the staging path is not reached here.
        public Task<PageNodeDto> CreateNodeForStagingAsync(
            Guid id, Guid? parentId, string segment, string path,
            string title, string pageType, int sortOrder, string? userId) =>
            throw new NotSupportedException("staging");

        public Task UpdateNodeForStagingAsync(
            Guid id, string segment, string path, string title, int sortOrder, string? userId) =>
            throw new NotSupportedException("staging");

        public Task ReplaceAllVersionsForStagingAsync(
            Guid nodeId, IReadOnlyList<PageNodeVersionDto> versions, string? userId) =>
            throw new NotSupportedException("staging");

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
            SortOrder = n.SortOrder,
            Title = n.Title,
            PageType = n.PageType
        };

        private static PageNodeVersionDto ToVersionDto(FakeVersion v) => new()
        {
            Id = v.Id,
            VersionId = v.VersionId,
            MinorVersion = v.MinorVersion,
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
            public int SortOrder { get; set; }
            public bool IsDeleted { get; set; }
            public DateTime CreatedDate { get; init; }
        }

        private sealed class FakeVersion
        {
            public Guid Id { get; init; }
            public Guid NodeId { get; init; }
            public int VersionId { get; init; }
            public int MinorVersion { get; set; }
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
