using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Validators;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public sealed class JourneyValidationServiceFormatFailureTests
{
    private const string OwnMessage = "Enter how many pupils have an incorrect grade for this qualification";

    private readonly WholeNumberFormatValidator _validator = new();
    private readonly JourneyValidationService _sut;

    public JourneyValidationServiceFormatFailureTests() =>
        _sut = new JourneyValidationService(formatValidators: [_validator]);

    private static Question CohortCount(string? formatFailure) => new()
    {
        Id = "q-cohort-count", Type = QuestionType.FreeText, Title = "How many?",
        Validator = "WholeNumber", ValidationFailure = "Enter a number", FormatFailure = formatFailure
    };

    private string? Validate(Question question, string value) =>
        _sut.ValidateAnswer(
            question, new QuestionAnswer { TextValue = value }, question.Title, question.ValidationFailure);

    [Fact]
    public void A_malformed_answer_gets_the_format_failure_of_the_question_when_it_has_one()
        => Assert.Equal(OwnMessage, Validate(CohortCount(OwnMessage), "abc"));

    [Fact]
    public void A_malformed_answer_gets_the_message_of_the_validator_when_the_question_has_none()
        => Assert.Equal(_validator.FailureMessage, Validate(CohortCount(null), "abc"));

    [Fact]
    public void A_well_formed_answer_passes()
        => Assert.Null(Validate(CohortCount(OwnMessage), "12"));
}
