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
    /// <summary>
    /// Open windows whose key stage this school does not take part in. A list rather than one
    /// joined sentence: the page prints a banner per window, so each can name its own window.
    /// </summary>
    public List<CheckingWindowDto> NotValidWindows { get; set; } = [];

    /// <summary>
    /// Open windows this school takes part in but holds no data for. A list rather than one joined
    /// sentence because the banner names a learner, and two windows here can disagree about the
    /// word — a 16-19 window says "student" where a KS4 one says "pupil".
    /// </summary>
    public List<CheckingWindowDto> NoDataWindows { get; set; } = [];
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

    /// <summary>AB#298317: the next chance to review data, shown as month + year on the landing banner and Check your pupil data. Null = not set.</summary>
    public DateTime? NextOpportunity { get; init; }
}

