using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

// AB#296648: results-enquiry enum members must exist, serialize within the
// ChangeRequest string-column limits (max 20), and be appended after existing
// members so stored values are unmoved.
public sealed class EnumContractTests
{
    [Fact]
    public void ResultsEnquiry_request_type_exists_and_fits_column()
    {
        var name = RequestType.ResultsEnquiry.ToString();
        Assert.Equal("ResultsEnquiry", name);
        Assert.True(name.Length <= 20);
        Assert.Equal(2, (int)RequestType.ResultsEnquiry); // appended after ConfirmCorrect
    }

    [Fact]
    public void IncorrectGrade_what_to_change_exists_and_fits_column()
    {
        var name = WhatToChange.IncorrectGrade.ToString();
        Assert.Equal("IncorrectGrade", name);
        Assert.True(name.Length <= 20);
        Assert.Equal(4, (int)WhatToChange.IncorrectGrade); // appended after Add
    }

    [Fact]
    public void Journey_engine_enums_gain_results_enquiry_members()
    {
        Assert.Equal(4, (int)PageType.ResultSearch);
        Assert.Equal(5, (int)PageType.ResultDetails);
        Assert.Equal(6, (int)QuestionType.GradeSelect);
        Assert.Equal(2, (int)NextSteps.ResultsEnquiry); // appended after Confirm
    }

    [Theory]
    [InlineData(WhatToChange.IncorrectGrade, "ResultsEnquiry")]
    [InlineData(WhatToChange.Merge, "PupilData")]
    [InlineData(WhatToChange.Remove, "PupilData")]
    public void WhatToChange_maps_to_its_checking_exercise(WhatToChange change, string exercise)
        => Assert.Equal(exercise, WhatToChangeCheckingExerciseMap.CheckingExerciseFor(change));
}
