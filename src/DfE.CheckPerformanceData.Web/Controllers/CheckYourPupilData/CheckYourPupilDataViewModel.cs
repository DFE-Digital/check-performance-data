using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

public sealed class CheckYourPupilDataViewModel
{
    public required string WindowId { get; init; }

    /// <summary>The page's pupil tables, in display order.</summary>
    public required IReadOnlyList<PupilTableSection> Sections { get; init; }

    /// <summary>
    /// True for KS4-style windows, where each section is its own tab. False for Post16, where all
    /// sections stack inside one "Pupils" tab.
    /// </summary>
    public required bool SectionsAsTabs { get; init; }

    public required string WindowTitle { get; init; }
    public NextSteps? SelectedNextStep { get; init; }

    /// <summary>
    /// Next-step options for the exercises open right now (#317), from
    /// <see cref="INextStepsService"/>. Empty means render no form at all — the tables, the search
    /// and the downloads stay, because a closed exercise removes actions, never content.
    ///
    /// Never defaulted from the request: the POST re-derives this so a hand-crafted post cannot
    /// start a journey for an exercise that is shut.
    /// </summary>
    public required IReadOnlyList<NextSteps> AvailableNextSteps { get; init; }

    /// <summary>
    /// The pupil-data exercise's own end date, from <c>EndDateFor(PupilData)</c> — never the outer
    /// window's, which on a multi-exercise window is months later and would promise a deadline the
    /// school does not have. Null when the window has no pupil-data exercise, in which case there
    /// is no deadline sentence to show.
    ///
    /// Like every checking-window date this is a UK wall-clock value, not a UTC instant, so it is
    /// formatted as-is and never routed through <c>LondonTime</c>.
    /// </summary>
    public DateTime? PupilDataEndDate { get; init; }

    /// <summary>
    /// Whether the pupil-data exercise is open, from <c>ICheckingExerciseService.IsOpen</c>. Drives
    /// the tense of the deadline sentence. The comparison against the clock belongs to that service
    /// alone, so the view must not re-derive this from <see cref="PupilDataEndDate"/>.
    /// </summary>
    public bool IsPupilDataOpen { get; init; }

    public required string OrganisationName { get; init; }
}
