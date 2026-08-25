using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// #320: the solution has one spelling of an exercise name — the enum. The map returned
// `const string` values only because it landed before CheckingExerciseType existed (#313), and it
// moved here from Application/ResultsEnquiry/ because it maps every WhatToChange member rather than
// just the enquiry one. Split out of EnumContractTests to sit beside what it maps to.
public sealed class WhatToChangeCheckingExerciseMapTests
{
    [Theory]
    [InlineData(WhatToChange.IncorrectGrade, CheckingExerciseType.ResultsEnquiry)]
    [InlineData(WhatToChange.Merge, CheckingExerciseType.PupilData)]
    [InlineData(WhatToChange.Remove, CheckingExerciseType.PupilData)]
    [InlineData(WhatToChange.Include, CheckingExerciseType.PupilData)]
    [InlineData(WhatToChange.Add, CheckingExerciseType.PupilData)]
    public void Each_change_type_maps_to_its_checking_exercise(
        WhatToChange change, CheckingExerciseType exercise)
        => Assert.Equal(exercise, WhatToChangeCheckingExerciseMap.CheckingExerciseFor(change));

    [Fact]
    public void Every_change_type_maps_to_an_exercise_that_exists()
    {
        // The map falls back to PupilData rather than throwing, so a new WhatToChange member is
        // gated as pupil data until someone says otherwise. This pins that the fallback is always a
        // real exercise, which is what the #318 journey gate and ClosedExerciseGuard both assume.
        foreach (WhatToChange change in Enum.GetValues<WhatToChange>())
        {
            Assert.True(Enum.IsDefined(WhatToChangeCheckingExerciseMap.CheckingExerciseFor(change)));
        }
    }
}
