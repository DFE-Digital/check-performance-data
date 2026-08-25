namespace DfE.CheckPerformanceData.Web.ViewComponents.Pager;

// The four things a pager can emit in order. Kept as an enum rather than a discriminated
// class hierarchy so the Razor partial can switch on a single field and unit tests can
// assert against a flat item list without allocating per-kind wrappers.
public enum PagerItemKind
{
    Previous,
    Next,
    Page,
    Ellipsis,
}
