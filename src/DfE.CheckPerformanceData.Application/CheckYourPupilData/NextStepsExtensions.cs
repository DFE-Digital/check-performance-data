namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// AB#298317: the one definition of "results enquiry is the only thing left open" — the state in
/// which Check your pupil data asks the designed Yes/No question and in which, alone, its POST
/// accepts <see cref="NextSteps.SignOut"/>. The view model and the controller both call this so
/// the question and its answer can never disagree about when they apply.
/// </summary>
public static class NextStepsExtensions
{
    /// <remarks>
    /// A list pattern rather than Count/indexer so a binder-created view model (null collection)
    /// answers false instead of throwing.
    /// </remarks>
    public static bool IsResultsEnquiryOnly(this IReadOnlyList<NextSteps>? steps) =>
        steps is [NextSteps.ResultsEnquiry];
}
