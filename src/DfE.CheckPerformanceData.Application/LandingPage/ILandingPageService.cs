using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.LandingPage;

public interface ILandingPageService
{
    Task<LandingPageResult?> GetLandingPageDataAsync(CancellationToken cancellationToken);
}

public sealed class LandingPageResult
{
    public required string OrganisationName { get; set; }
    public required string OrganisationLaestab { get; set; }
    public required string OrganisationUrn { get; set; }
    public required List<OrganisationKeyStageDto> KeyStages { get; set; }
    public required List<CheckingWindowDto> OpenWindows { get; set; }
    public string? NotValidWindowsText { get; set; }
    public string? NoDataWindowsText { get; set; }
    public string OrganisationAddress { get; set; } = string.Empty;
}

public sealed class CheckingWindowDto
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public required DateTime EndDate { get; init; }
    public required KeyStages KeyStage { get; init; }
    public required CheckingWindowType CheckingWindowType { get; init; }
    public bool HasPupilData { get; init; }
    public required DateTime StartDate { get; init; }

    /// <summary>
    /// The window's checking exercises, in sort order. Pass this to
    /// <see cref="ICheckingExerciseService"/> to ask whether a given exercise is open — the outer
    /// StartDate/EndDate above only say whether the window as a whole is running, and a Post16
    /// window runs pupil data checking and results enquiry on different ranges inside it.
    /// Only the exercise dates are projected here; the landing page has no use for the datasets.
    /// </summary>
    public List<CheckingExerciseDto> Exercises { get; init; } = [];
    public string TurnaroundCommitment { get; init; } = string.Empty;
}

