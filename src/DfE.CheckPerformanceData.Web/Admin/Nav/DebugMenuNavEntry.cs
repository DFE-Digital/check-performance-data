using DfE.CheckPerformanceData.Application.Settings;

namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Tile linking the development-only tooling pages (dev pipeline trigger, dev Zendesk outbox,
// queue-seed), shown under the System administration group. Enabled is driven by the
// Dev:ToolsEnabled config flag so the tile is hidden wherever the dev endpoints themselves
// 404, keeping the menu and the routes it links in step. Sits as a sibling tile in the
// two-level admin nav, not a nested sub-item.
public sealed record DebugMenuNavEntry(IConfiguration Configuration) : IAdminNavEntry
{
    public string Key => AdminNavKeys.DebugMenu;
    public string? ParentKey => AdminNavKeys.SystemAdmin;
    public string Title => "Debug";
    public string Description => "Development-only pipeline tools and the Zendesk outbox.";
    public string Url => "/dev/zendesk/outbox";
    public bool Enabled => Configuration.GetValue<bool>(SettingKeys.DevToolsEnabled);
    public int Order => 90;
}
