namespace DfE.CheckPerformanceData.Application.ContentPages;

// Maps a region layout to its GDS grid column classes — one per column, in order. This is the
// single place the renderer turns a layout into markup, and the closed mapping is what keeps a
// region's layout safe (a layout can only ever produce these known classes, never arbitrary CSS).
public static class RegionLayoutGrid
{
    private static readonly IReadOnlyDictionary<RegionLayout, IReadOnlyList<string>> Classes =
        new Dictionary<RegionLayout, IReadOnlyList<string>>
        {
            [RegionLayout.Single] = ["govuk-grid-column-full"],
            [RegionLayout.Halves] = ["govuk-grid-column-one-half", "govuk-grid-column-one-half"],
            [RegionLayout.Thirds] =
                ["govuk-grid-column-one-third", "govuk-grid-column-one-third", "govuk-grid-column-one-third"],
            [RegionLayout.Quarters] =
            [
                "govuk-grid-column-one-quarter", "govuk-grid-column-one-quarter",
                "govuk-grid-column-one-quarter", "govuk-grid-column-one-quarter"
            ],
            [RegionLayout.OneThirdTwoThirds] = ["govuk-grid-column-one-third", "govuk-grid-column-two-thirds"],
            [RegionLayout.TwoThirdsOneThird] = ["govuk-grid-column-two-thirds", "govuk-grid-column-one-third"]
        };

    public static IReadOnlyList<string> ColumnClasses(RegionLayout layout) => Classes[layout];

    public static int ColumnCount(RegionLayout layout) => Classes[layout].Count;
}
