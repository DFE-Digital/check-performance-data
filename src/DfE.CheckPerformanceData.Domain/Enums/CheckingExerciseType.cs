using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Domain.Enums;

/// <summary>
/// The activities a checking window can run. A window type with one activity has one exercise; a
/// window type with several has several, each on its own dates. Adding a member here must never
/// need a schema change — see docs/16-19-window-model.md.
/// </summary>
public enum CheckingExerciseType
{
    [Display(Name = "Pupil data checking")]
    PupilData,
    [Display(Name = "Results enquiry")]
    ResultsEnquiry
}
