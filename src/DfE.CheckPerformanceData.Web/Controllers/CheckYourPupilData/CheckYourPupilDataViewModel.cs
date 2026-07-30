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
    public required string OrganisationName { get; init; }
}
