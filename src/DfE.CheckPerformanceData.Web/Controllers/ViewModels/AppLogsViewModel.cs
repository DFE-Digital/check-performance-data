using DfE.CheckPerformanceData.Application.Logging;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class AppLogsViewModel
{
    public required IReadOnlyList<AppLogDto> Rows { get; init; }
    public required int Total { get; init; }
    public required IReadOnlyList<string> Levels { get; init; }
    public required IReadOnlyList<string> Categories { get; init; }
    public required int Skip { get; init; }
    public required int Take { get; init; }

    // Echoed filters so the form re-populates on submit.
    public string? Level { get; init; }
    public string? Category { get; init; }
    public string? Search { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }

    public int Page => (Skip / Math.Max(Take, 1)) + 1;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)Math.Max(Take, 1)));
}
