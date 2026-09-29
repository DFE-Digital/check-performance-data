using DfE.CheckPerformanceData.Application.Journey.Validators;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// Issue 496: the "permanent-exclusion-dfe-number" question ("What is the DfE number of the school
// which permanently excluded {pupil}?") must not reuse the generic DfE-number error wording. Its
// failures must carry the pinned design copy instead. The value rules are the shared
// DfeNumberFormatValidator's; only the name and the message are this question's own.
public class PermanentExclusionDfeNumberFormatValidatorTests
{
    private readonly PermanentExclusionDfeNumberFormatValidator _sut = new();

    private const string DesignMessage =
        "Enter the 7 digit DfE number of the school which permanently excluded the pupil";

    [Fact]
    public void Name_is_the_name_the_flow_config_references()
    {
        // The engine fails OPEN on an unresolved validator name — the format check is simply skipped
        // — so this string is load-bearing and never silently wrong.
        Assert.Equal("PermanentExclusionDfeNumber", _sut.Name);
    }

    [Fact]
    public void The_failure_message_is_the_pinned_design_copy()
    {
        // The engine reports validator.FailureMessage, not the question's validationFailure, so the
        // two must read identically or the user sees different copy for empty vs malformed (FR-004:
        // a single message for every failure).
        Assert.Equal(DesignMessage, _sut.FailureMessage);
    }

    [Theory]
    [InlineData("123/4567")]
    [InlineData("1234567")]
    public void A_valid_dfe_number_is_accepted(string value)
        => Assert.True(_sut.IsValid(value));

    [Theory]
    [InlineData("12/4567")]      // too few leading digits
    [InlineData("1234/567")]     // wrong split
    [InlineData("1234")]         // too short
    [InlineData("12345678")]     // too long
    [InlineData("123-4567")]     // wrong separator
    [InlineData("abc")]          // non-numeric
    [InlineData("123 4567")]     // space instead of slash
    [InlineData(" 1234567")]     // leading whitespace
    [InlineData("1234567 ")]     // trailing whitespace
    [InlineData("")]
    [InlineData("   ")]
    public void Anything_else_is_invalid(string value)
        => Assert.False(_sut.IsValid(value));
}