using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class ConfirmationViewModel
{
    public Guid WindowId { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? WindowCloseLabel { get; init; }

    /// <summary>
    /// The onward journeys this window has open right now for the signed-in school, from
    /// <see cref="INextStepsService.GetConfirmationPageSteps"/> (#713). Empty is a valid answer:
    /// the page then offers only its fixed links.
    /// </summary>
    public IReadOnlyList<NextSteps> NextSteps { get; init; } = [];

    /// <summary>"student" on 16-19, "pupil" elsewhere, derived from the window in session.</summary>
    public LearnerNoun LearnerNoun { get; init; } = LearnerNoun.Pupil;
}
