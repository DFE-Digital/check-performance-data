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

    public required string WindowEndDate { get; init; }
    public required string WindowEndTime { get; init; }
    public required string WindowTitle { get; init; }
    public NextSteps? SelectedNextStep { get; init; }
    public required bool IsWindowOpen { get; init; }

    /// <summary>
    /// AB#296648: whether to offer "Report an issue with an exam result" alongside the amend/confirm
    /// options. 16-19 only for now — no other key stage has results data or a flow config behind it.
    ///
    /// PARKED: visibility moves to <c>IWindowActivityService.OpenActivities</c> when the activity model
    /// lands (docs/16-19-window-model.md), at which point results enquiry gets its own dates and this
    /// stops being a straight window-type test. Not defaulted from the request — the POST re-derives it
    /// so a hand-crafted post cannot bypass the rule.
    /// </summary>
    public bool ShowResultsEnquiryOption { get; init; }
    public required string OrganisationName { get; init; }
}
