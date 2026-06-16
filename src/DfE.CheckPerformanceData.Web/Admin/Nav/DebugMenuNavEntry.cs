using DfE.CheckPerformanceData.Application.Settings;

namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Tile linking the development-only tooling pages (dev pipeline trigger, dev Zendesk outbox,
// queue-seed), nested under the Pipeline dashboard entry in the rules-engine sub-tree.
// Enabled is driven by the Dev:ToolsEnabled config flag so the tile is hidden wherever the
// dev endpoints themselves 404, keeping the menu and the routes it links in step.
public sealed record DebugMenuNavEntry(IConfiguration Configuration) : IAdminNavEntry
{
    public string Key => AdminNavKeys.DebugMenu;
    public string? ParentKey => AdminNavKeys.Observability;
    public string Title => "Debug Pipelines";
    public string Description => "The UAT test console: drive the pipeline and tick off acceptance checks.";
    public string Url => "/dev/uat";
    public bool Enabled => Configuration.GetValue<bool>(SettingKeys.DevToolsEnabled);
    public int Order => 10;
}
