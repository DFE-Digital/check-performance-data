using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

// AB#301913: the one place that decides "is this the grade the result already holds". The
// validator uses it to refuse a posted no-op and the view-model builder uses it to keep the
// current grade out of the picker; a single definition is what stops those two ever disagreeing.
public sealed class GradeEqualityTests
{
    [Theory]
    [InlineData("5", "5")]
    [InlineData(" 5", "5 ")]
    [InlineData("24F", "24F")]
    public void The_same_grade_is_the_same_grade(string candidate, string current)
        => Assert.True(GradeEquality.IsSame(candidate, current));

    [Theory]
    [InlineData("4", "5")]
    // Ordinal and case-sensitive: grades are opaque codes, and on the IB Diploma 24F is a fail
    // while 24D is a pass, so one character is a real change.
    [InlineData("24D", "24F")]
    [InlineData("m1", "M1")]
    // Sharing a prefix is not sameness.
    [InlineData("A", "A*")]
    public void A_different_grade_is_not_the_same_grade(string candidate, string current)
        => Assert.False(GradeEquality.IsSame(candidate, current));

    [Theory]
    [InlineData(null, "5")]
    [InlineData("", "5")]
    [InlineData("   ", "5")]
    [InlineData("5", null)]
    [InlineData("5", "")]
    [InlineData("5", "  ")]
    [InlineData(null, null)]
    public void Nothing_is_the_same_as_an_unknown_grade(string? candidate, string? current)
        => Assert.False(GradeEquality.IsSame(candidate, current));
}
