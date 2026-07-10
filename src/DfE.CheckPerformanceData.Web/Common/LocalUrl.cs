namespace DfE.CheckPerformanceData.Web.Common;

/// <summary>
/// Open-redirect guard for user-supplied return URLs. A value is "safe local" only when it is a
/// path rooted at a single '/', which rules out absolute URLs (e.g. https://…) and protocol-relative
/// values ('//host' or '/\host'). Returns null for anything unsafe or empty so each caller supplies
/// its own fallback.
/// </summary>
public static class LocalUrl
{
    public static string? SafeOrNull(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url[0] != '/') return null;
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\')) return null;
        return url;
    }
}
