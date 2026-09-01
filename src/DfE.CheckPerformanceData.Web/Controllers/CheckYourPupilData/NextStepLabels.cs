using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

/// <summary>
/// The on-screen wording for each next-step option. Presentation copy, so it lives in Web — which
/// exercise offers which option is the Application layer's business (<see cref="INextStepsService"/>).
/// </summary>
/// <remarks>
/// The label doubles as a button caption when only one option survives, so it is written as an
/// instruction the user can act on rather than as a noun phrase.
/// </remarks>
public static class NextStepLabels
{
    public static string For(NextSteps step, LearnerNoun noun) => step switch
    {
        NextSteps.RequestChange => $"Request an amendment to {noun.Singular} data",
        NextSteps.Confirm => $"Confirm {noun.Singular} data is correct",
        NextSteps.ResultsEnquiry => "Report an issue with an exam result",
        _ => step.ToString()
    };
}
