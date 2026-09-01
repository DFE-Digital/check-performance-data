using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Web.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class ValidationErrorCodingTests
{
    private static Question Q(QuestionType type) => new() { Id = "q", Type = type, Title = "T" };

    [Fact]
    public void Unanswered_question_is_required()
    {
        Assert.Equal("required", ValidationErrorCoding.ForQuestion(Q(QuestionType.TextArea), isAnswered: false));
    }

    [Theory]
    [InlineData(QuestionType.Date, "bad_date")]
    [InlineData(QuestionType.TextArea, "too_long")]
    [InlineData(QuestionType.Autocomplete, "selection_invalid")]
    [InlineData(QuestionType.Radio, "selection_invalid")]
    // A rejected checkbox value means the option was not one this user was shown — a bad
    // selection, not a bad format.
    [InlineData(QuestionType.Checkbox, "selection_invalid")]
    public void Answered_but_invalid_maps_by_type(QuestionType type, string expected)
    {
        Assert.Equal(expected, ValidationErrorCoding.ForQuestion(Q(type), isAnswered: true));
    }
}
