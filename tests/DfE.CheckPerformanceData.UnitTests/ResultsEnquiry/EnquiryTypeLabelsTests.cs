using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public sealed class EnquiryTypeLabelsTests
{
    [Theory]
    [InlineData(WhatToChange.IncorrectGrade, CheckingWindowType.Post16, "Incorrect grade")]
    [InlineData(WhatToChange.IncorrectGrade, CheckingWindowType.KS4Autumn, "Incorrect grade")]
    [InlineData(WhatToChange.MissingQualification, CheckingWindowType.Post16, "Missing qualification")]
    [InlineData(WhatToChange.MissingQualification, CheckingWindowType.KS4Autumn, "Missing qualification")]
    [InlineData(WhatToChange.ResultDoesNotBelong, CheckingWindowType.Post16, "Result does not belong to student")]
    [InlineData(WhatToChange.ResultDoesNotBelong, CheckingWindowType.KS4Autumn, "Result does not belong to pupil")]
    public void An_enquiry_type_is_named_with_the_learner_noun_of_the_window(
        WhatToChange type, CheckingWindowType windowType, string expected)
        => Assert.Equal(expected, EnquiryTypeLabels.For(type, LearnerNoun.For(windowType)));

    [Fact]
    public void The_stored_description_starts_with_the_results_enquiry_prefix()
        => Assert.Equal(
            "Results enquiry - Result does not belong to pupil",
            EnquiryTypeLabels.DescriptionFor(
                WhatToChange.ResultDoesNotBelong, LearnerNoun.For(CheckingWindowType.KS4Autumn)));

    [Fact]
    public void Every_results_enquiry_type_has_a_label()
    {
        var enquiryTypes = Enum.GetValues<WhatToChange>()
            .Where(type => WhatToChangeCheckingExerciseMap.IsResultsEnquiry(type));

        Assert.All(enquiryTypes, type =>
            Assert.False(string.IsNullOrWhiteSpace(EnquiryTypeLabels.For(type, LearnerNoun.Pupil))));
    }

    [Fact]
    public void A_change_that_is_not_a_results_enquiry_has_no_label()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => EnquiryTypeLabels.For(WhatToChange.Remove, LearnerNoun.Pupil));
}
