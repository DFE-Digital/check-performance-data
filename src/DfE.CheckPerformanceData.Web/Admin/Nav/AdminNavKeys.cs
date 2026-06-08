namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Stable identifiers for admin nav entries. Group entries use CmsAdmin / SystemAdmin
// as their Key (with ParentKey = null). Tile entries reference one of those values
// via their ParentKey so the controller can resolve children at render time without
// string-typing the relationship.
public static class AdminNavKeys
{
    public const string CmsAdmin = "cms-admin";
    public const string SystemAdmin = "system-admin";
    public const string VersionRetention = "version-retention";
    public const string ContentStaging = "content-staging";
    public const string DeletedPages = "deleted-pages";
    public const string SeedSamplePages = "seed-sample-pages";
    public const string CmsSettings = "cms-settings";
    public const string VrDashboard = "vr-dashboard";
    public const string RulesConfig = "rules-config";
    public const string StorageAdmin = "storage-admin";
    public const string StorageBrowser = "storage-browser";
}
