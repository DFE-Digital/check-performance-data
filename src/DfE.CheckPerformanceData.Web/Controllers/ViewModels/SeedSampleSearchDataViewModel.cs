namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// View model for the seed-sample-search-data GET form. Preset + advanced overrides + the
// (nullable) success banner text so the same view renders both the empty form and the
// post-submit page in one round-trip.
public sealed class SeedSampleSearchDataViewModel
{
    // Selected preset key from the radio group. Defaults to "24h" on first paint per T4.
    public string Preset { get; init; } = "24h";

    // Optional advanced-overrides — null means "use the preset default".
    public int? EventCount { get; init; }
    public int? MessageCount { get; init; }

    // Populated by the POST action's redirect handoff via TempData. Rendered as a GDS
    // success banner above the form when non-null. Kept for the JS-disabled fallback path
    // where the initial POST is still 302 -> banner, though the primary UX is now the
    // progress modal.
    public string? SuccessBanner { get; init; }

    // Populated when the URL carries ?jobId=… — the page reloads mid-seed OR the JS-less
    // POST hand-off. Non-null means "render the modal auto-open marker and let the JS
    // pick up the poll cycle from this id".
    public Guid? ActiveJobId { get; init; }
}
