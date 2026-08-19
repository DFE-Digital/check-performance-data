using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Option A of docs/16-19-window-model.md: each <see cref="WhatToChange"/> member belongs to
/// one checking exercise. The future ICheckingExerciseService gating consumes this; nothing else
/// may hardcode the mapping. String values (not the not-yet-built CheckingExerciseType
/// enum) so this ticket does not depend on the checking-exercise model landing first.
/// </summary>
public static class WhatToChangeCheckingExerciseMap
{
    public const string PupilData = "PupilData";
    public const string ResultsEnquiry = "ResultsEnquiry";

    public static string CheckingExerciseFor(WhatToChange change) => change switch
    {
        WhatToChange.IncorrectGrade => ResultsEnquiry,
        _ => PupilData
    };
}
