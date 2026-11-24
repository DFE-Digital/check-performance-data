namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// How the media library page lists its images. The choice travels in the query string as
/// <c>view</c>. A value that is not one of the three shows the grid.
/// </summary>
public static class MediaLibraryView
{
    public const string Grid = "grid";
    public const string Details = "details";
    public const string Large = "large";

    public sealed record Option(string Value, string Label);

    public static IReadOnlyList<Option> All { get; } =
    [
        new(Grid, "Grid"),
        new(Details, "Details"),
        new(Large, "Large"),
    ];

    public static string Normalise(string? view) =>
        All.FirstOrDefault(o => string.Equals(o.Value, view, StringComparison.OrdinalIgnoreCase))?.Value ?? Grid;
}
