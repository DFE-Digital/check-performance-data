using DfE.CheckPerformanceData.Application.Journey.Validators;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class NcnValidatorTests
{
    private readonly NcnValidator _sut = new();

    [Theory]
    [InlineData("12345")]
    [InlineData("1")]
    [InlineData("A1B2C")]
    public void Accepts_five_characters_or_fewer(string value) => Assert.True(_sut.IsValid(value));

    [Theory]
    [InlineData("123456")]
    [InlineData("  123456  ")] // whitespace is not content; trim before measuring
    public void Rejects_more_than_five_characters(string value) => Assert.False(_sut.IsValid(value));

    [Fact]
    public void The_failure_message_is_the_tickets_exact_copy()
    {
        // AB#298201 pins this string; the generic "{title} must be N characters or less" message
        // would lead with the whole field label and read wrong.
        Assert.Equal("National Centre Number (NCN) must be 5 characters or less", _sut.FailureMessage);
    }
}
