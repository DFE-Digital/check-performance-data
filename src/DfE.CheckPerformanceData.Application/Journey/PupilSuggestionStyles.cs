using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Application.Journey;

public static class PupilSuggestionStyles
{
    /// <summary>
    /// The row style for the pupil search pages of a journey: the results enquiry row for a results
    /// enquiry, the standard row for each other journey and for no journey.
    /// </summary>
    public static PupilSuggestionStyle For(WhatToChange? change) =>
        WhatToChangeCheckingExerciseMap.IsResultsEnquiry(change)
            ? PupilSuggestionStyle.ResultsEnquiry
            : PupilSuggestionStyle.Standard;
}
