namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public enum NextSteps
{
    RequestChange,
    Confirm,
    // AB#296648: the 16-19 way in to the results-enquiry journey from the check-your-pupil-data
    // page. Appended last so existing values are unmoved.
    ResultsEnquiry
}
