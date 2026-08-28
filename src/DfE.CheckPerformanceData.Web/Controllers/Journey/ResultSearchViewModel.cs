using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// The "Which of {pupil}'s results is incorrect?" page (AB#296648). Mirrors
/// <see cref="PupilSearchViewModel"/>: an accessible-autocomplete over a JSON suggestions endpoint,
/// with a hidden field carrying the machine-readable selection alongside the human-readable label.
/// </summary>
public sealed class ResultSearchViewModel
{
    public Guid WindowId { get; set; }
    public string PageId { get; set; } = string.Empty;

    /// <summary>Title with <c>{pupilName}</c> resolved — safe to render, but not into the page title.</summary>
    public string Title { get; set; } = string.Empty;

    public string? Hint { get; set; }

    /// <summary>
    /// The composite key (<c>QAN|SESSION|SOURCE</c>) of the current selection, re-rendered on a
    /// validation redisplay so a valid choice is not silently lost. AB#295434's lesson.
    /// </summary>
    public string? SelectedResultKey { get; set; }

    /// <summary>The resolved result, shown as a confirmation summary once one is chosen.</summary>
    public StudentResultRecord? SelectedResult { get; set; }

    /// <summary>
    /// Every result the selected pupil holds, rendered as real <c>&lt;option&gt;</c> elements so the
    /// page works with JavaScript off. accessible-autocomplete then enhances the select in place. A
    /// pupil holds a handful of results, so there is nothing to gain from fetching them as the user
    /// types — and a server-rendered list is the only version that is accessible without script.
    /// </summary>
    public IReadOnlyList<StudentResultRecord> AvailableResults { get; set; } = [];

    public string? BackPageId { get; set; }

    /// <summary>The JourneyController action that serves <see cref="BackPageId"/>.</summary>
    public string BackPageAction { get; set; } = nameof(JourneyController.Page);
}
