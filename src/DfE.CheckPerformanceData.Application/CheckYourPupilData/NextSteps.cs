namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public enum NextSteps
{
    RequestChange,
    Confirm,
    // AB#296648: the 16-19 way in to the results-enquiry journey from the check-your-pupil-data
    // page. Appended last so existing values are unmoved.
    ResultsEnquiry,
    // AB#298317: "No, I'd like to sign out of this service" — the other answer to the question
    // Check your pupil data asks once results enquiry is the only open exercise. Not a journey:
    // INextStepsService never offers it, and the controller accepts it only in that state.
    SignOut
}
