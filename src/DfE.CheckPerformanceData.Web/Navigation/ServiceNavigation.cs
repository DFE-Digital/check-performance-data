using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http.Features;

namespace DfE.CheckPerformanceData.Web.Navigation;

/// <summary>
/// Finds the active section of the school-facing service navigation (#854).
/// </summary>
public static class ServiceNavigation
{
    /// <summary>
    /// Returns the section from the endpoint's <see cref="ServiceNavSectionAttribute"/>, or null
    /// when the endpoint has none. A journey page takes Amendment requests when the session is in
    /// single-edit or bulk-edit mode for the route's window. Else it takes Pupils.
    /// </summary>
    public static ServiceNavSection? ActiveSection(HttpContext context)
    {
        var section = context.GetEndpoint()?.Metadata.GetMetadata<ServiceNavSectionAttribute>()?.Section;
        if (section is not ServiceNavSection.Journey)
            return section;

        return IsEditingDraft(context) ? ServiceNavSection.AmendmentRequests : ServiceNavSection.Pupils;
    }

    /// <summary>
    /// True when the request path is the link's path. The compare ignores case and a trailing
    /// slash. The active item then takes <c>aria-current="page"</c>, not <c>"true"</c>.
    /// </summary>
    public static bool IsCurrentPage(HttpRequest request, string? href)
    {
        if (string.IsNullOrEmpty(href))
            return false;

        var path = request.PathBase.Add(request.Path).Value ?? "";
        return string.Equals(path.TrimEnd('/'), href.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEditingDraft(HttpContext context)
    {
        if (context.Features.Get<ISessionFeature>()?.Session is not { } session)
            return false;
        if (!Guid.TryParse(context.Request.RouteValues["windowId"]?.ToString(), out var windowId))
            return false;

        return session.IsSingleEditMode(windowId) || session.IsBulkEditMode(windowId);
    }
}
