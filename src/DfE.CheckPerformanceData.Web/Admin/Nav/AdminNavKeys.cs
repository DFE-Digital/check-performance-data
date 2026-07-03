namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Stable identifiers for admin nav entries. Group entries use CmsAdmin / SystemAdmin
// as their Key (with ParentKey = null). Tile entries reference one of those values
// via their ParentKey so the controller can resolve children at render time without
// string-typing the relationship.
public static class AdminNavKeys
{
    public const string CmsAdmin = "cms-admin";
    public const string SystemAdmin = "system-admin";
    public const string AmendmentRequestsAdmin = "amendment-requests-admin";
    public const string UncommittedRequests = "uncommitted-requests";
    public const string ContentStaging = "content-staging";
    public const string ContentPages = "content-pages";
    public const string ContentBlocks = "content-blocks";
    public const string DeletedPages = "deleted-pages";
    public const string SeedSamplePages = "seed-sample-pages";
    public const string SystemSettings = "system-settings";
    public const string RoleSettings = "role-settings";
    public const string RulesConfig = "rules-config";

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
}
