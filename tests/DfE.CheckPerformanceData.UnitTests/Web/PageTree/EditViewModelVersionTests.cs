using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Models.Guidance;
using DfE.CheckPerformanceData.Web.Models.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.PageTree;

// Unit tests for PublishedVersionId and DraftVersionId computed properties on both edit VMs.
// These properties derive from the Versions list so are testable without any DB or Razor engine.
public sealed class EditViewModelVersionTests
{
    private static PageNodeVersionDto Live(int id) => new()
    {
        VersionId = id, IsCurrent = true,
        PublishFrom = DateTime.UtcNow.AddDays(-1), Content = string.Empty
    };

    private static PageNodeVersionDto WorkingDraft(int id) => new()
    {
        VersionId = id, IsCurrent = false, PublishFrom = null, Content = string.Empty
    };

    private static PageNodeVersionDto Scheduled(int id) => new()
    {
        VersionId = id, IsCurrent = false,
        PublishFrom = DateTime.UtcNow.AddDays(1), Content = string.Empty
    };

    // ── ContentPageEditViewModel ──────────────────────────────────────────────

    // live v2 + working-draft v3 (v3 PublishFrom null) → Published=2, Draft=3
    [Fact]
    public void ContentVm_LivePlusWorkingDraft_ReturnsCorrectVersionIds()
    {
        var vm = MakeContentVm([WorkingDraft(3), Live(2)]);
        Assert.Equal(2, vm.PublishedVersionId);
        Assert.Equal(3, vm.DraftVersionId);
    }

    // only v1 with PublishFrom null → Published=null, Draft=1
    [Fact]
    public void ContentVm_OnlyWorkingDraft_PublishedIsNull_DraftIsV1()
    {
        var vm = MakeContentVm([WorkingDraft(1)]);
        Assert.Null(vm.PublishedVersionId);
        Assert.Equal(1, vm.DraftVersionId);
    }

    // only v1 scheduled future (PublishFrom set, IsCurrent false) → Published=null, Draft=1 (latest surfaced)
    [Fact]
    public void ContentVm_OnlyScheduledVersion_PublishedIsNull_DraftSurfacesLatest()
    {
        var vm = MakeContentVm([Scheduled(1)]);
        Assert.Null(vm.PublishedVersionId);
        Assert.Equal(1, vm.DraftVersionId);
    }

    // live v2, no newer draft (v2 IsCurrent, window set; no null-window version) → Published=2, Draft=null
    [Fact]
    public void ContentVm_LiveWithNoWorkingDraft_DraftIsNull()
    {
        var vm = MakeContentVm([Live(2)]);
        Assert.Equal(2, vm.PublishedVersionId);
        Assert.Null(vm.DraftVersionId);
    }

    [Fact]
    public void ContentVm_EmptyVersions_BothIdsNull()
    {
        var vm = MakeContentVm([]);
        Assert.Null(vm.PublishedVersionId);
        Assert.Null(vm.DraftVersionId);
    }

    // ── PageTreeAdminWikiEditViewModel ────────────────────────────────────────

    [Fact]
    public void WikiVm_LivePlusWorkingDraft_ReturnsCorrectVersionIds()
    {
        var vm = MakeWikiVm([WorkingDraft(3), Live(2)]);
        Assert.Equal(2, vm.PublishedVersionId);
        Assert.Equal(3, vm.DraftVersionId);
    }

    [Fact]
    public void WikiVm_OnlyWorkingDraft_PublishedIsNull_DraftIsV1()
    {
        var vm = MakeWikiVm([WorkingDraft(1)]);
        Assert.Null(vm.PublishedVersionId);
        Assert.Equal(1, vm.DraftVersionId);
    }

    [Fact]
    public void WikiVm_OnlyScheduledVersion_PublishedIsNull_DraftSurfacesLatest()
    {
        var vm = MakeWikiVm([Scheduled(1)]);
        Assert.Null(vm.PublishedVersionId);
        Assert.Equal(1, vm.DraftVersionId);
    }

    [Fact]
    public void WikiVm_LiveWithNoWorkingDraft_DraftIsNull()
    {
        var vm = MakeWikiVm([Live(2)]);
        Assert.Equal(2, vm.PublishedVersionId);
        Assert.Null(vm.DraftVersionId);
    }

    [Fact]
    public void WikiVm_EmptyVersions_BothIdsNull()
    {
        var vm = MakeWikiVm([]);
        Assert.Null(vm.PublishedVersionId);
        Assert.Null(vm.DraftVersionId);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ContentPageEditViewModel MakeContentVm(IReadOnlyList<PageNodeVersionDto> versions) =>
        new()
        {
            ActionBase = "/admin/pages/test/content",
            Title      = "Test",
            Content    = Array.Empty<ContentNode>(),
            Versions   = versions
        };

    private static PageTreeAdminWikiEditViewModel MakeWikiVm(IReadOnlyList<PageNodeVersionDto> versions) =>
        new() { NodeTitle = "Test", Versions = versions };
}
