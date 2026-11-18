using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Common;

public sealed class SpreadsheetFormulaTests
{
    [Theory]
    [InlineData("=1+1")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tx")]
    [InlineData("\rx")]
    [InlineData("\nx")]
    [InlineData("＝1+1")]
    [InlineData("＋1")]
    [InlineData("－1")]
    [InlineData("＠x")]
    public void Text_that_starts_with_a_trigger_character_gets_an_apostrophe(string text)
    {
        Assert.True(SpreadsheetFormula.StartsWithTrigger(text));
        Assert.Equal("'" + text, SpreadsheetFormula.Neutralise(text));
    }

    [Theory]
    [InlineData("-1.00")]
    [InlineData("+3")]
    [InlineData("-0")]
    [InlineData("-.5")]
    [InlineData("-12.")]
    [InlineData("-123456789012345678901234567890.5")]
    [InlineData("-1.5E-07")]
    [InlineData("-1e5")]
    [InlineData("+2E+3")]
    public void A_signed_number_stays_as_it_is(string number)
    {
        Assert.True(SpreadsheetFormula.StartsWithTrigger(number));
        Assert.Equal(number, SpreadsheetFormula.Neutralise(number));
    }

    [Theory]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData("-.")]
    [InlineData("-1+1")]
    [InlineData("-1,000")]
    [InlineData("- 1")]
    [InlineData("-1 ")]
    [InlineData("-1\n")]
    [InlineData("-1.2.3")]
    [InlineData("--1")]
    [InlineData("+-1")]
    [InlineData("-1%")]
    [InlineData("-1e")]
    [InlineData("-e5")]
    [InlineData("-1e5+1")]
    [InlineData("-١")]
    public void Signed_text_that_is_not_a_number_gets_an_apostrophe(string text) =>
        Assert.Equal("'" + text, SpreadsheetFormula.Neutralise(text));

    [Theory]
    [InlineData("")]
    [InlineData("Smith")]
    [InlineData("1.5")]
    [InlineData("x=1")]
    [InlineData("A-B")]
    [InlineData(" =1+1")]
    [InlineData("'=1+1")]
    public void Text_that_does_not_start_with_a_trigger_character_stays_as_it_is(string text)
    {
        Assert.False(SpreadsheetFormula.StartsWithTrigger(text));
        Assert.Equal(text, SpreadsheetFormula.Neutralise(text));
    }
}
