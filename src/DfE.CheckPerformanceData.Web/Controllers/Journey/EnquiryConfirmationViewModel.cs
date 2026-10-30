using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// The "Results enquiry submitted" page (AB#296648). Deliberately minimal: after submission the
/// journey state is cleared, so the page has only the reference to show — everything the school
/// entered has gone, which is what makes "report an issue" start clean. #713 adds the onward
/// links: whatever the window has open now, decided by INextStepsService.
/// </summary>
public sealed class EnquiryConfirmationViewModel
{
    public required Guid WindowId { get; init; }
    public required string ReferenceNumber { get; init; }

    /// <summary>
    /// The onward journeys this window has open right now for the signed-in school, from
    /// <see cref="INextStepsService.GetConfirmationPageSteps"/> (#713). Empty is a valid answer:
    /// the page then offers only "Return to check your data".
    /// </summary>
    public IReadOnlyList<NextSteps> NextSteps { get; init; } = [];

    /// <summary>"student" on 16-19, "pupil" elsewhere, derived from the window in session.</summary>
    public LearnerNoun LearnerNoun { get; init; } = LearnerNoun.Pupil;
}
