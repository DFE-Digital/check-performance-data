namespace DfE.CheckPerformanceData.Application.PageTree;

/// <summary>
/// Derives human-readable version labels from a list of versions.
/// Drafts (MinorVersion >= 1) display as "{major}.{minor:00}"; published versions (MinorVersion == 0)
/// display as "{major}". Major is derived from publish order, not stored.
/// </summary>
public static class PageVersionNumbering
{
    /// <summary>
    /// Returns the major number for a version within the given ordered list.
    /// For a published version (Minor == 0): count of published versions with VersionId &lt;= own.
    /// For a draft (Minor >= 1): count of published versions with VersionId &lt; own.
    /// </summary>
    public static int MajorFor(IReadOnlyList<PageNodeVersionDto> all, PageNodeVersionDto v) =>
        v.MinorVersion == 0
            ? all.Count(x => x.MinorVersion == 0 && x.VersionId <= v.VersionId)
            : all.Count(x => x.MinorVersion == 0 && x.VersionId < v.VersionId);

    /// <summary>
    /// Returns the display label: "{major}" for published versions, "{major}.{minor:00}" for drafts.
    /// </summary>
    public static string Label(IReadOnlyList<PageNodeVersionDto> all, PageNodeVersionDto v)
    {
        var major = MajorFor(all, v);
        return v.MinorVersion == 0 ? major.ToString() : $"{major}.{v.MinorVersion:00}";
    }
}
