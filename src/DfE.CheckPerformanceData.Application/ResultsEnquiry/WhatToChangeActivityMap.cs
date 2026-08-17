using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Option A of docs/16-19-window-model.md: each <see cref="WhatToChange"/> member belongs to
/// one window activity. The future IWindowActivityService gating consumes this; nothing else
/// may hardcode the mapping. String values (not the not-yet-built CheckingWindowActivityType
/// enum) so this ticket does not depend on the activity model landing first.
/// </summary>
public static class WhatToChangeActivityMap
{
    public const string PupilData = "PupilData";
    public const string ResultsEnquiry = "ResultsEnquiry";

    public static string ActivityFor(WhatToChange change) => change switch
    {
        WhatToChange.IncorrectGrade => ResultsEnquiry,
        _ => PupilData
    };
}
