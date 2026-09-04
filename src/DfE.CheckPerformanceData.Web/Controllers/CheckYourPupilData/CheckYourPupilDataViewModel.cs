using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;

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

    /// <summary>
    /// AB#298317: whether the results-enquiry exercise is open, from
    /// <c>ICheckingExerciseService.IsOpen</c>. Chooses the closing sentence of the closed-window
    /// paragraph ("report any issues" versus "view and download").
    /// </summary>
    public bool IsResultsEnquiryOpen { get; init; }

    /// <summary>
    /// AB#298317: the window's next opportunity to review data, already formatted as month + year
    /// by <c>NextOpportunityText</c>. Null when the admin has not set it, and then the sentence
    /// that names it is omitted.
    /// </summary>
    public string? NextOpportunity { get; init; }

    /// <summary>
    /// AB#298317: results enquiry is the only thing left open — the state in which the page asks
    /// "Would you like to report an issue with an exam result?" with a Yes/No answer instead of
    /// offering a one-item form. A list pattern rather than Count/indexer so a binder-created
    /// instance (null collection) answers false instead of throwing — see the LearnerNoun remarks.
    /// </summary>
    public bool OffersEnquiryOnly => AvailableNextSteps is [NextSteps.ResultsEnquiry];

    public required string OrganisationName { get; init; }

    /// <summary>
    /// The word this window uses for a learner — "student" on 16-19, "pupil" everywhere else.
    /// Assembled in the controller from the window type so the view never looks it up.
    /// </summary>
    /// <remarks>
    /// Defaulted rather than <c>required</c>, and this matters: the same class is the model-binding
    /// target for the NextStep POST. <c>required</c> is a compile-time rule only — the binder
    /// creates the instance through its parameterless constructor and sets nothing — and MVC's
    /// validation visitor then reads every property on the bound object, <see cref="Title"/>
    /// included. A null here threw a NullReferenceException before the action ran.
    ///
    /// "pupil" is the safe default for the same reason it is elsewhere: it is the word every key
    /// stage but 16-19 uses, and the POST does not render a noun anyway — it re-derives the window
    /// and rebuilds the model when it has to redisplay the page.
    /// </remarks>
    public LearnerNoun LearnerNoun { get; init; } = LearnerNoun.Pupil;

    /// <summary>
    /// The CMS key for the page's <c>&lt;h1&gt;</c>, scoped to the window type so each key stage
    /// seeds and edits its own heading. See <c>WindowScopedContentKey</c>.
    /// </summary>
    /// <remarks>
    /// Still <c>required</c>, unlike <see cref="LearnerNoun"/> above: nothing dereferences it, so a
    /// binder-created instance leaving it null hurts nobody, and a plausible-but-wrong default would
    /// quietly hand one window type another's content block.
    /// </remarks>
    public required string TitleContentKey { get; init; }

    /// <summary>
    /// The page heading, and therefore also <c>ViewBag.Title</c> — the two must stay identical
    /// (WCAG 2.4.2), so both read this one value rather than each spelling the noun themselves.
    /// </summary>
    public string Title => $"Check your {LearnerNoun.Singular} data";
}
