namespace DfE.CheckPerformanceData.Web.Admin;

/// <summary>
/// The one place that decides whether a blob name, folder prefix or uploaded file name taken
/// from a request may reach the storage SDK.
/// </summary>
/// <remarks>
/// Every storage-browser route builds a blob path from request input. The SDK turns that path
/// into a URL, and URL handling normalises dot segments, so a name is only safe if it has no
/// <c>.</c> or <c>..</c> segment at all. The same check rejects separators and control
/// characters that have no business in a name, and the 1,024-character ceiling is Azure's own.
/// A name that fails here cannot be a real blob, so callers answer 404, the same answer a
/// protected container gets.
/// </remarks>
public static class StorageBlobNames
{
    public const int MaxLength = 1024;

    /// <summary>A full blob name: at least one segment, never ends in a slash.</summary>
    public static bool IsValidBlobName(string? name) =>
        !string.IsNullOrEmpty(name) && !name.EndsWith('/') && SegmentsAreSafe(name);

    /// <summary>A folder prefix: empty (the root), or safe segments with or without a trailing slash.</summary>
    public static bool IsValidPrefix(string? prefix) =>
        string.IsNullOrEmpty(prefix) || SegmentsAreSafe(prefix.TrimEnd('/'));

    /// <summary>Empty for the root, otherwise the prefix ending in exactly one slash.</summary>
    public static string NormalizePrefix(string? prefix)
    {
        var trimmed = prefix?.Trim().Trim('/');
        return string.IsNullOrEmpty(trimmed) ? string.Empty : $"{trimmed}/";
    }

    /// <summary>
    /// The blob name an upload lands at: &lt;prefix&gt;/&lt;optional folder&gt;/&lt;leaf file name&gt;,
    /// or null when any part is unsafe.
    /// </summary>
    public static string? ResolveUploadName(string? prefix, string? folder, string? fileName)
    {
        if (!IsValidPrefix(prefix?.Trim())) return null;

        var subFolder = folder?.Trim().Trim('/');
        if (!string.IsNullOrEmpty(subFolder) && !IsValidPrefix(subFolder)) return null;

        // Browsers send the path as typed on the uploader's machine, so both separators are
        // stripped here whatever platform this runs on.
        var leaf = string.IsNullOrEmpty(fileName)
            ? null
            : fileName[(Math.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf('\\')) + 1)..];
        if (string.IsNullOrEmpty(leaf) || leaf.Contains('/') || !IsValidBlobName(leaf)) return null;

        var target = NormalizePrefix(prefix);
        if (!string.IsNullOrEmpty(subFolder)) target += $"{subFolder}/";
        target += leaf;
        return IsValidBlobName(target) ? target : null;
    }

    private static bool SegmentsAreSafe(string value)
    {
        if (value.Length == 0 || value.Length > MaxLength || value[0] == '/') return false;

        foreach (var c in value)
        {
            if (c < ' ' || c == '\u007f' || c == '\\') return false;
        }

        foreach (var segment in value.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..") return false;
        }

        return true;
    }
}
