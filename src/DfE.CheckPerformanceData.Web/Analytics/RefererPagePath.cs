using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Web.Analytics;

/// <summary>
/// Derives a trustworthy <c>page_path</c> analytics value from the Referer header
/// (AB#286387 whole-branch review Finding 2). Shared by
/// <see cref="DfE.CheckPerformanceData.Web.Controllers.ClientEventsController"/> and
/// <see cref="DfE.CheckPerformanceData.Web.Controllers.ContactController"/>, which
/// previously duplicated an inline <c>Uri.TryCreate(...).AbsolutePath</c> that
/// accepted any absolute URI (not just same-origin) with no length cap.
/// </summary>
public static class RefererPagePath
{
    /// <summary>
    /// Matches <c>ClientEventsController.MaxTextLength</c>, the existing cap already
    /// applied to <c>ExpandText</c> on the same beacon request — reusing it here keeps
    /// every client-influenced analytics string field bounded by the same limit.
    /// </summary>
    public const int MaxLength = 100;

    /// <summary>
    /// Returns the Referer header's path, truncated to <see cref="MaxLength"/>, or
    /// <see langword="null"/> when the header is missing, not a well-formed absolute
    /// URI, or not same-origin with <paramref name="request"/>. Same-origin is
    /// checked via <c>Authority</c>/<c>Host.Value</c> equality, matching the existing
    /// convention in <c>ContactController.ResolveOpenerReturnUrl</c>.
    /// </summary>
    public static string? From(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer))
        {
            return null;
        }

        if (!string.Equals(referer.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = referer.AbsolutePath;
        return path.Length > MaxLength ? path[..MaxLength] : path;
    }
}
