using DfE.CheckPerformanceData.Application.Journey.Validators;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class DfeNumberFormatValidatorTests
{
    private readonly DfeNumberFormatValidator _sut = new();

    [Fact]
    public void Name_IsDfeNumber()
    {
        Assert.Equal("DfeNumber", _sut.Name);
    }

    [Fact]
    public void FailureMessage_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(_sut.FailureMessage));
    }

    [Theory]
    [InlineData("123/4567")]
    [InlineData("1234567")]
    [InlineData("502/1353")]
    public void IsValid_WhenFormatMatches_ReturnsTrue(string value)
    {
        Assert.True(_sut.IsValid(value));
    }

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
    [InlineData("")]             // empty
    public void IsValid_WhenFormatDoesNotMatch_ReturnsFalse(string value)
    {
        Assert.False(_sut.IsValid(value));
    }
}
