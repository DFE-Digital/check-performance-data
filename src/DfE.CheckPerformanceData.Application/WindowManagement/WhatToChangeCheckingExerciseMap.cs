using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// Option A of docs/16-19-window-model.md: each <see cref="WhatToChange"/> member belongs to
/// one checking exercise. <see cref="ICheckingExerciseService"/> gating consumes this; nothing else
/// may hardcode the mapping.
/// </summary>
/// <remarks>
/// <para>
/// #318: this returned the exercise name as a string, because the map landed before
/// <see cref="CheckingExerciseType"/> existed. It returns the enum now, so a journey's exercise can
/// be handed straight to the gate without a name lookup that could drift from the enum. There are
/// no exercise-name string constants left anywhere in the solution.
/// </para>
/// <para>
/// #320 moved it here from <c>Application/ResultsEnquiry/</c>. It maps every
/// <see cref="WhatToChange"/> member, not just the enquiry one, so it belongs beside
/// <see cref="ICheckingExerciseService"/> and <see cref="WindowExercises"/> — everything that
/// answers a question about checking exercises lives in one namespace.
/// </para>
/// </remarks>
public static class WhatToChangeCheckingExerciseMap
{
    public static CheckingExerciseType CheckingExerciseFor(WhatToChange change) => change switch
    {
        WhatToChange.IncorrectGrade => CheckingExerciseType.ResultsEnquiry,
        WhatToChange.MissingQualification => CheckingExerciseType.ResultsEnquiry,
        WhatToChange.ResultDoesNotBelong => CheckingExerciseType.ResultsEnquiry,
        _ => CheckingExerciseType.PupilData
    };

    /// <summary>
    /// AB#298704: the one predicate for "is this journey a results enquiry". Guards that used to
    /// list members (`is IncorrectGrade or MissingQualification`) missed every new kind — the
    /// AB#298229 "guard names one enum member" defect class — so they resolve through the map now.
    /// </summary>
    public static bool IsResultsEnquiry(WhatToChange? change) =>
        change is { } c && CheckingExerciseFor(c) == CheckingExerciseType.ResultsEnquiry;
}
