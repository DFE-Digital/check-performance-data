using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public sealed class GradedExamQualificationsTests
{
    private const string RslMusicPerformanceGrade8 = "5010648X";
    private const string GcseMathematics = "60146084";

    [Fact]
    public void The_list_has_the_33_qans_from_the_spec()
    {
        Assert.Equal(33, GradedExamQualifications.All.Count);
    }

    [Theory]
    [InlineData("50103908")]
    [InlineData("50106454")]
    [InlineData("5010648X")]
    [InlineData("60138245")]
    [InlineData("60138257")]
    [InlineData("60138269")]
    public void Each_rsl_qan_is_a_graded_exam(string qan)
    {
        Assert.True(GradedExamQualifications.Contains(qan));
    }

    [Theory]
    [InlineData("5010648x")]
    [InlineData(" 5010648X ")]
    public void The_check_ignores_case_and_spaces_around_the_qan(string qan)
    {
        Assert.True(GradedExamQualifications.Contains(qan));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(GcseMathematics)]
    public void A_qan_that_is_not_on_the_list_is_not_a_graded_exam(string? qan)
    {
        Assert.False(GradedExamQualifications.Contains(qan));
    }

    [Theory]
    [InlineData(RslMusicPerformanceGrade8, true)]
    [InlineData(GcseMathematics, false)]
    public void A_ks4_autumn_enquiry_has_an_answer(string qan, bool expected)
    {
        Assert.Equal(expected, GradedExamQualifications.ForEnquiry(CheckingWindowType.KS4Autumn, qan));
    }

    [Theory]
    [InlineData(CheckingWindowType.Post16)]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS2)]
    [InlineData(null)]
    public void Another_window_type_has_no_answer(CheckingWindowType? windowType)
    {
        Assert.Null(GradedExamQualifications.ForEnquiry(windowType, RslMusicPerformanceGrade8));
    }
}
