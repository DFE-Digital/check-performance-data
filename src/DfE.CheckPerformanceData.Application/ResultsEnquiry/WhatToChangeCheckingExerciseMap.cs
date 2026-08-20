using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Option A of docs/16-19-window-model.md: each <see cref="WhatToChange"/> member belongs to
/// one checking exercise. <see cref="WindowManagement.ICheckingExerciseService"/> gating consumes
/// this; nothing else may hardcode the mapping.
/// </summary>
/// <remarks>
/// #318: this returned the exercise name as a string, because the map landed before
/// <see cref="CheckingExerciseType"/> existed. It returns the enum now, so a journey's exercise can
/// be handed straight to the gate without a name lookup that could drift from the enum.
/// </remarks>
public static class WhatToChangeCheckingExerciseMap
{
    public static CheckingExerciseType CheckingExerciseFor(WhatToChange change) => change switch
    {
        WhatToChange.IncorrectGrade => CheckingExerciseType.ResultsEnquiry,
        _ => CheckingExerciseType.PupilData
    };
}
