using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Models.Guidance;
using DfE.CheckPerformanceData.Web.Models.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.PageTree;

// Unit tests for PublishedVersionLabel and DraftVersionLabel computed properties on both edit VMs.
// These properties derive from the Versions list so are testable without any DB or Razor engine.
// Versions use MinorVersion to determine draft vs published:
//   MinorVersion == 0 → integer (published) version
//   MinorVersion >= 1 → draft
public sealed class EditViewModelVersionTests
{
    // Live version: MinorVersion=0, IsCurrent=true
    private static PageNodeVersionDto Live(int id) => new()
    {
        VersionId = id, IsCurrent = true, MinorVersion = 0,
        PublishFrom = DateTime.UtcNow.AddDays(-1), Content = string.Empty
    };

    // Working draft: MinorVersion=2 (as if saved twice after the last publish)
    private static PageNodeVersionDto WorkingDraft(int id) => new()
    {
        VersionId = id, IsCurrent = false, MinorVersion = 2,
        PublishFrom = null, Content = string.Empty
    };

    // Scheduled: MinorVersion=0 (UpdateVersionWindowAsync with from!=null zeros minor)
    private static PageNodeVersionDto Scheduled(int id) => new()
    {
        VersionId = id, IsCurrent = false, MinorVersion = 0,
        PublishFrom = DateTime.UtcNow.AddDays(1), Content = string.Empty
    };

    // ── ContentPageEditViewModel ──────────────────────────────────────────────

    // live v2 + working-draft v3 → Published="1", Draft="2" (the draft will publish as v2).
    [Fact]
    public void ContentVm_LivePlusWorkingDraft_ReturnsCorrectLabels()
    {
        var vm = MakeContentVm([WorkingDraft(3), Live(2)]);
        Assert.Equal("1", vm.PublishedVersionLabel);
        Assert.Equal("2", vm.DraftVersionLabel);
    }

    // First-ever draft on a brand-new page → Published=null, Draft="1".
    [Fact]
    public void ContentVm_OnlyWorkingDraft_PublishedIsNull_DraftIs_1()
    {
        var vm = MakeContentVm([new PageNodeVersionDto
        {
            VersionId = 1, IsCurrent = false, MinorVersion = 1,
            PublishFrom = null, Content = string.Empty
        }]);
        Assert.Null(vm.PublishedVersionLabel);
        Assert.Equal("1", vm.DraftVersionLabel);
    }

    // only v1 scheduled (Minor=0) → Published=null, Draft surfaced as latest → label "1"
    [Fact]
    public void ContentVm_OnlyScheduledVersion_PublishedIsNull_DraftSurfacesLatest()
    {
        var vm = MakeContentVm([Scheduled(1)]);
        Assert.Null(vm.PublishedVersionLabel);
        // No Minor>=1 draft; published is also null → latest surfaced: v1 Minor=0 → "1"
        Assert.Equal("1", vm.DraftVersionLabel);
    }

    // live v2 only (no draft) → Published="1", Draft=null
    [Fact]
    public void ContentVm_LiveWithNoWorkingDraft_DraftIsNull()
    {
        var vm = MakeContentVm([Live(2)]);
        Assert.Equal("1", vm.PublishedVersionLabel);
        Assert.Null(vm.DraftVersionLabel);
    }

    [Fact]
    public void ContentVm_EmptyVersions_BothLabelsNull()
    {
        var vm = MakeContentVm([]);
        Assert.Null(vm.PublishedVersionLabel);
        Assert.Null(vm.DraftVersionLabel);
    }

    // ── PageTreeAdminWikiEditViewModel ────────────────────────────────────────

    [Fact]
    public void WikiVm_LivePlusWorkingDraft_ReturnsCorrectLabels()
    {
        var vm = MakeWikiVm([WorkingDraft(3), Live(2)]);
        Assert.Equal("1", vm.PublishedVersionLabel);
        Assert.Equal("2", vm.DraftVersionLabel);
    }

    [Fact]
    public void WikiVm_OnlyWorkingDraft_PublishedIsNull_DraftIs_1()
    {
        var vm = MakeWikiVm([new PageNodeVersionDto
        {
            VersionId = 1, IsCurrent = false, MinorVersion = 1,
            PublishFrom = null, Content = string.Empty
        }]);
        Assert.Null(vm.PublishedVersionLabel);
        Assert.Equal("1", vm.DraftVersionLabel);
    }

    [Fact]
    public void WikiVm_OnlyScheduledVersion_PublishedIsNull_DraftSurfacesLatest()
    {
        var vm = MakeWikiVm([Scheduled(1)]);
        Assert.Null(vm.PublishedVersionLabel);
        Assert.Equal("1", vm.DraftVersionLabel);
    }

    [Fact]
    public void WikiVm_LiveWithNoWorkingDraft_DraftIsNull()
    {
        var vm = MakeWikiVm([Live(2)]);
        Assert.Equal("1", vm.PublishedVersionLabel);
        Assert.Null(vm.DraftVersionLabel);
    }

    [Fact]
    public void WikiVm_EmptyVersions_BothLabelsNull()
    {
        var vm = MakeWikiVm([]);
        Assert.Null(vm.PublishedVersionLabel);
        Assert.Null(vm.DraftVersionLabel);
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
