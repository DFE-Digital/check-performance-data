namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Stable identifiers for admin nav entries. Group entries use CmsAdmin / SystemAdmin
// as their Key (with ParentKey = null). Tile entries reference one of those values
// via their ParentKey so the controller can resolve children at render time without
// string-typing the relationship.
public static class AdminNavKeys
{
    public const string Dashboard = "dashboard";
    public const string CmsAdmin = "cms-admin";
    public const string SystemAdmin = "system-admin";
    public const string ContentStaging = "content-staging";
    public const string ContentPages = "content-pages";
    public const string ContentBlocks = "content-blocks";
    public const string DeletedPages = "deleted-pages";
    public const string SeedSamplePages = "seed-sample-pages";
    public const string SystemSettings = "system-settings";
    public const string RoleSettings = "role-settings";
    public const string RulesConfig = "rules-config";
    public const string AppLogs = "app-logs";

    public const string RulesEngineGroup = "rules-engine-group";
    public const string RulesEngine = "rules-engine";
    public const string RulesEngineQueue = "rules-engine-queue";
    public const string ZendeskQueue = "zendesk-queue";
    public const string DeadLetterQueue = "dead-letter-queue";
    public const string Observability = "observability";
    public const string StorageAdmin = "storage-admin";
    public const string StorageBrowser = "storage-browser";
    public const string Transactions = "transactions";
    public const string ReplaySubmissions = "replay-submissions";

    public const string DangerZone = "danger-zone";
    public const string ResetSeedData = "reset-seed-data";

    // Wallboard / share-token management. No sidebar nav entry today — the surface is reached
    // from the observability dashboard — but the key exists so the section can be gated through
    // the AdminSectionAccess grid alongside every other admin surface.
    public const string ShareAdmin = "share-admin";

    // Window administration. All three are in DefaultAdminAccessSeeder.AllSections: FilterByAccess
    // checks each tile's own Key, so without a grant the whole group stayed invisible in the
    // sidebar even though the entries were registered. ManageWindow additionally gates the
    // per-window requests page (AdminRequestsController), which is reached from that table.
    public const string WindowAdmin = "window-admin";
    public const string NewWindow = "new-window";
    public const string ManageWindow = "manage-window";

    // Search-analytics admin surface. Downstream plans hang [RequireAdminSection] off
    // this const, so the seeder MUST have a matching entry in AllSections or the gate
    // returns 404 on a fresh DB.
    public const string SearchAnalytics = "search-analytics";
    public const string MessagesInbox = "messages-inbox";

    // Top-level container that groups every incoming-message surface: the search-feedback
    // inbox and the dead-letter queue. Group entries carry no [RequireAdminSection] gate
    // themselves — access to the group is implied by access to at least one child, so this
    // key is intentionally absent from DefaultAdminAccessSeeder.AllSections.
    public const string MessagesGroup = "messages-group";

    // Sub-group nested under System administration. Holds the two "populate the local
    // database with plausible content" tiles — Seed sample CMS pages and Seed sample
    // search data. Unlike the CMS / System containers, this key IS in
    // DefaultAdminAccessSeeder.AllSections so the TestDataController class-level
    // [RequireAdminSection] gate has a matching grant on a fresh DB.
    public const string TestDataGroup = "test-data-group";

    // Live admin nav tile under the Test data group. Renders the seed-sample-search-data
    // form + POST action for pumping plausible search events + feedback messages into the
    // sink so the search-analytics dashboard has something to show during demos.
    public const string SeedSampleSearchData = "seed-sample-search-data";
}
