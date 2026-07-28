namespace DfE.CheckPerformanceData.Web.ViewComponents.Pager;

// One entry in the rendered pager. PageNumber is null for ellipses and for disabled
// Previous/Next controls (there is no page to link to). IsCurrent + IsDisabled drive the
// visual state — the current page anchor gets aria-current="page" and a distinct GDS
// treatment; disabled Prev/Next render without an anchor so screen readers do not read
// them as interactive.
public sealed record PagerItem(
    PagerItemKind Kind,
    int? PageNumber = null,
    bool IsCurrent = false,
    bool IsDisabled = false);
