using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Web.PageTree;

/// <summary>
/// Validates a proposed page-tree URL path before a <c>PageNode</c> is created.
/// Checks three things:
/// <list type="bullet">
///   <item>The path is non-empty and matches the allowed character set.</item>
///   <item>The first segment is not in the hard-reserved set (<c>admin</c>, <c>dev</c>, <c>healthcheck</c>).</item>
///   <item>The first segment is not already claimed by a registered application route.</item>
/// </list>
/// Uniqueness of the full path within existing <c>PageNode</c> rows is enforced by the
/// controller (against the repository) and is intentionally out of scope here.
/// </summary>
public sealed class PageNodePathValidator
{
    // Lower-case slug pattern: one or more alphanum groups separated by hyphens,
    // zero or more additional slash-separated segments of the same shape.
    private static readonly Regex PathPattern =
        new(@"^[a-z0-9]+(-[a-z0-9]+)*(/[a-z0-9]+(-[a-z0-9]+)*)*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlySet<string> HardReserved =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "admin", "dev", "healthcheck" };

    private readonly IReservedRouteProvider _reserved;

    public PageNodePathValidator(IReservedRouteProvider reserved)
        => _reserved = reserved;

    /// <summary>
    /// Validates <paramref name="path"/>.
    /// </summary>
    /// <returns>
    /// <c>(true, null)</c> when the path is acceptable;
    /// <c>(false, errorMessage)</c> with a human-readable reason when it is not.
    /// </returns>
    public (bool ok, string? error) Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "Path must not be empty.");

        if (!PathPattern.IsMatch(path))
            return (false, "Path may only contain lowercase letters, digits, hyphens, and forward slashes (e.g. 'my-page' or 'section/sub-page').");

        var firstSegment = path.Split('/')[0];

        if (HardReserved.Contains(firstSegment))
            return (false, $"'{firstSegment}' is a reserved path segment and cannot be used for content pages.");

        if (!DefaultPageNodeRoots.Segments.Contains(firstSegment)
            && _reserved.ReservedFirstSegments().Contains(firstSegment))
            return (false, $"'{firstSegment}' conflicts with an existing application route and cannot be used for content pages.");

        return (true, null);
    }
}
