using DfE.CheckPerformanceData.Application.Journey.Validators;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#296648: the cohort-count question ("how many students have an incorrect grade for this
// qualification?"). A count is a plain whole number — a decimal, a sign or a thousands separator is
// a typo, not a quantity.
public class WholeNumberFormatValidatorTests
{
    private readonly WholeNumberFormatValidator _sut = new();

    [Fact]
    public void Name_is_the_name_the_flow_config_references()
    {
        // The engine fails OPEN on an unresolved validator name — the format check is simply skipped
        // — so this string is load-bearing and never silently wrong.
        Assert.Equal("WholeNumber", _sut.Name);
    }

    [Fact]
    public void The_failure_message_is_the_cohort_count_copy()
    {
        // The engine reports validator.FailureMessage, not the question's validationFailure, so the
        // two must read identically or the user sees different copy for empty vs malformed.
        Assert.Equal("Enter how many students have an incorrect grade for this qualification", _sut.FailureMessage);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("10")]
    [InlineData("9999")]
    [InlineData(" 10 ")]      // surrounding whitespace is a typing artefact, not a different answer
    public void A_whole_number_between_one_and_9999_is_valid(string value)
        => Assert.True(_sut.IsValid(value));

    [Theory]
    [InlineData("0")]         // a cohort of nobody is not an enquiry
    [InlineData("-3")]
    [InlineData("+3")]
    [InlineData("2.5")]
    [InlineData("2,5")]
    [InlineData("1 000")]     // internal whitespace
    [InlineData("1,000")]     // thousands separator
    [InlineData("ten")]
    [InlineData("10a")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("10000")]     // above the cap
    [InlineData("00")]        // zero however it is written
    public void Anything_else_is_invalid(string value)
        => Assert.False(_sut.IsValid(value));

    [Theory]
    [InlineData("01")]
    [InlineData("0010")]
    public void A_leading_zero_is_accepted_because_the_value_it_denotes_is_in_range(string value)
        => Assert.True(_sut.IsValid(value));

    [Fact]
    public void A_number_far_beyond_int_range_is_invalid_rather_than_throwing()
        => Assert.False(_sut.IsValid(new string('9', 40)));
}
