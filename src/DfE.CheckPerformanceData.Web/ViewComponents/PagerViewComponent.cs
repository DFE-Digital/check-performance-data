using System.Collections.Generic;
using DfE.CheckPerformanceData.Web.ViewComponents.Pager;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.ViewComponents;

// Single reusable pager for every admin paged surface. Truncates a long page list to
// first-edge + middle-window + last-edge separated by ellipses so long series (a 34-page
// bucket drill-in, a 145-page volume drill-in, an inbox with hundreds of messages)
// stay on one line instead of wrapping across the viewport.
//
// Callers pass a currentPage / totalPages pair plus a Func that turns a page number
// into the URL the anchor should link to — that indirection is what lets each callsite
// keep its own query string (range/bucket/sort/filter/reference/etc) intact when only
// the page number changes.
public sealed class PagerViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        int currentPage,
        int totalPages,
        Func<int, string> urlBuilder,
        int edgeCount = PagerModel.DefaultEdgeCount,
        int visibleNeighbours = PagerModel.DefaultVisibleNeighbours,
        string ariaLabel = "Pagination")
    {
        ArgumentNullException.ThrowIfNull(urlBuilder);

        var items = PagerModel.BuildItems(currentPage, totalPages, edgeCount, visibleNeighbours);
        var renderItems = new List<PagerRenderItem>(items.Count);
        foreach (var item in items)
        {
            var href = item.PageNumber.HasValue ? urlBuilder(item.PageNumber.Value) : null;
            renderItems.Add(new PagerRenderItem(item, href));
        }

        return View(new PagerRenderModel(renderItems, ariaLabel));
    }
}

// Rendering-side model — attaches the resolved href to each PagerItem so the Razor
// partial can stay declarative.
public sealed record PagerRenderModel(IReadOnlyList<PagerRenderItem> Items, string AriaLabel);

public sealed record PagerRenderItem(PagerItem Item, string? Href);
