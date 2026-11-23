using System.Reflection;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers.WindowAdmin;

// #800: the window rules page is read-only, but it shows the whole shared rules config.
// It is therefore gated by both the window grant (class level) and the rules-config grant
// (method level). These tests pin both, the way QueueAdminControllerTests pins its gate.

public sealed class WindowRulesControllerTests
{
    [Fact]
    public void Controller_Gated_By_ManageWindow_Section()
    {
        var gate = typeof(WindowRulesController).GetCustomAttribute<RequireAdminSectionAttribute>();
        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.ManageWindow, gate!.SectionKey);
    }

    [Fact]
    public void Index_Gated_By_RulesConfig_Section()
    {
        var method = typeof(WindowRulesController).GetMethod(nameof(WindowRulesController.Index));
        Assert.NotNull(method);
        var gate = method!.GetCustomAttribute<RequireAdminSectionAttribute>();
        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.RulesConfig, gate!.SectionKey);
    }
}
