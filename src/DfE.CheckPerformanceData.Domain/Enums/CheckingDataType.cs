namespace DfE.CheckPerformanceData.Domain.Enums;

/// <summary>
/// What kind of data an exercise holds. It names the per-school output file, so a window can hold
/// several exercises whose files never collide. Not the same axis as CheckingExerciseType, which
/// says what a school may *do*; a display-only exercise has a data type and no exercise type.
/// </summary>
public enum CheckingDataType
{
    Pupil,
    Results,
    PreviouslyPublished,
    ValueAdded,
    Other
}
