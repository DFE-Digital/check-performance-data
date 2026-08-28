using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class ValidateOptionSelectTests
{
    private readonly JourneyValidationService _sut = new();
    private static readonly Question Syllabus = new()
        { Id = "q-syllabus-code", Type = QuestionType.SyllabusSelect, Title = "Select syllabus code" };

    private static QuestionAnswer Answer(string? v) => new() { TextValue = v };

    [Fact]
    public void Blank_returns_the_resolved_failure() =>
        Assert.Equal("Select the syllabus code",
            _sut.ValidateOptionSelect(Syllabus, Answer(null), ["8300H"], "Select the syllabus code"));

    [Fact]
    public void A_value_not_in_the_offered_set_is_rejected_with_the_same_message()
    {
        // Fail closed: a forged POST must read exactly like no selection, not leak that the value
        // was "nearly right".
        Assert.Equal("Select the syllabus code",
            _sut.ValidateOptionSelect(Syllabus, Answer("FORGED"), ["8300H"], "Select the syllabus code"));
    }

    [Fact]
    public void An_empty_offered_set_rejects_everything()
    {
        // Only 13 of 974 QANs carry syllabus codes — an empty set must hold the enquiry back
        // (the page explains the gap), never wave a value through.
        Assert.NotNull(_sut.ValidateOptionSelect(Syllabus, Answer("8300H"), [], "Select the syllabus code"));
    }

    [Fact]
    public void Membership_is_ordinal_and_case_sensitive() =>
        Assert.NotNull(_sut.ValidateOptionSelect(Syllabus, Answer("8300h"), ["8300H"], "Select the syllabus code"));

    [Fact]
    public void An_offered_value_passes() =>
        Assert.Null(_sut.ValidateOptionSelect(Syllabus, Answer("8300H"), ["8300H"], "Select the syllabus code"));

    [Fact]
    public void With_no_resolved_failure_the_title_based_default_is_used() =>
        Assert.Equal("Select syllabus code is required",
            _sut.ValidateOptionSelect(Syllabus, Answer(null), ["8300H"]));
}
