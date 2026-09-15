using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The one place that says what each output type is: which amendment journey feeds it, what it is
/// called, how its file is named and which LDS correction type it carries. Every switch throws on
/// an unmapped member so a new EgressOutputType cannot ship half-wired.
/// </summary>
public static class EgressOutputTypes
{
    public static readonly IReadOnlyList<EgressOutputType> All =
        [EgressOutputType.NewLearners, EgressOutputType.RemoveLearners];

    public static WhatToChange WhatToChangeFor(EgressOutputType type) => type switch
    {
        EgressOutputType.NewLearners => WhatToChange.Add,
        EgressOutputType.RemoveLearners => WhatToChange.Remove,
        _ => throw Unmapped(type)
    };

    public static string Label(EgressOutputType type) => type switch
    {
        EgressOutputType.NewLearners => "New learners",
        EgressOutputType.RemoveLearners => "Remove learners",
        _ => throw Unmapped(type)
    };

    public static string FileToken(EgressOutputType type) => type switch
    {
        EgressOutputType.NewLearners => "NewLearners",
        EgressOutputType.RemoveLearners => "RemoveLearners",
        _ => throw Unmapped(type)
    };

    // LDS correction types (AB#292610): 10 = new learner, 31 = remove learner. The Zendesk field
    // carries them as "31_" — the trailing underscore never reaches the output.
    public static string CorrectionType(EgressOutputType type) => type switch
    {
        EgressOutputType.NewLearners => "10",
        EgressOutputType.RemoveLearners => "31",
        _ => throw Unmapped(type)
    };

    // The "stage" in the file name. 16-19 is KS5 to LDS even though nothing else in this codebase
    // calls it that (CheckingWindowType.Post16, KeyStages.Post16).
    public static string StageToken(CheckingWindowType windowType) => windowType switch
    {
        CheckingWindowType.KS4June => "KS4",
        CheckingWindowType.KS4Autumn => "KS4",
        CheckingWindowType.KS2 => "KS2",
        CheckingWindowType.Post16 => "KS5",
        _ => throw new ArgumentOutOfRangeException(nameof(windowType), windowType,
            "This window type has no LDS stage token. Add it to EgressOutputTypes.StageToken before egressing it.")
    };

    public static string FileName(CheckingWindowType windowType, EgressOutputType type, DateOnly exportDate) =>
        $"CYPMD_LDS_{StageToken(windowType)}_{FileToken(type)}_{exportDate:yyyy_MM_dd}.csv";

    private static ArgumentOutOfRangeException Unmapped(EgressOutputType type) =>
        new(nameof(type), type, "This output type is not mapped. Add it to every switch in EgressOutputTypes before offering it on the Pull page.");
}
