using System.Diagnostics;
using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Common;

public sealed class SpreadsheetFormulaTests
{
    private const int LongRunLength = 250;
    private static readonly TimeSpan QuickCheckLimit = TimeSpan.FromSeconds(2);

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tx")]
    [InlineData("\rx")]
    [InlineData("\nx")]
    [InlineData("\uFF1D1+1")]
    [InlineData("\uFF0B1")]
    [InlineData("\uFF0D1")]
    [InlineData("\uFF20x")]
    [InlineData("=")]
    [InlineData("@")]
    [InlineData("\uFF0B")]
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
    [InlineData("-5%")]
    [InlineData("+12.5%")]
    [InlineData("-1.10 (-2.35 to 0.15)")]
    [InlineData("+1.70 (0.45 to 2.95)")]
    [InlineData("-0.20 (-1.45 to -0.05)")]
    public void A_signed_value_in_a_supplier_form_stays_as_it_is(string value)
    {
        Assert.True(SpreadsheetFormula.StartsWithTrigger(value));
        Assert.Equal(value, SpreadsheetFormula.Neutralise(value));
    }

    [Theory]
    [InlineData("-.")]
    [InlineData("-1+1")]
    [InlineData("-1,000")]
    [InlineData("- 1")]
    [InlineData("-1 ")]
    [InlineData("-1\n")]
    [InlineData("-1.2.3")]
    [InlineData("--1")]
    [InlineData("+-1")]
    [InlineData("-1e")]
    [InlineData("-e5")]
    [InlineData("-1e5+1")]
    [InlineData("-\u0661")]
    [InlineData("--")]
    [InlineData("-\n")]
    [InlineData("-%")]
    [InlineData("-1%%")]
    [InlineData("-1 %")]
    [InlineData("-5%\n")]
    [InlineData("-1.10 (-2.35 to 0.15")]
    [InlineData("-1.10(-2.35 to 0.15)")]
    [InlineData("-1.10 (-2.35 TO 0.15)")]
    [InlineData("-1.10 (-2.35 to 0.15) ")]
    [InlineData("-1.10 (-2.35 to 0.15)\n")]
    [InlineData("-1.10 (=1+1 to 0.15)")]
    [InlineData("-1.10 (A1 to 0.15)")]
    [InlineData("-1.10 (-2.35 to 0.15)+cmd|' /C calc'!A0")]
    public void Signed_text_that_is_not_in_a_supplier_form_gets_an_apostrophe(string text) =>
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

    [Fact]
    public void A_long_cell_that_is_not_in_a_supplier_form_is_checked_quickly()
    {
        string digits = new('1', LongRunLength);
        string text = $"-{digits} ({digits} to {digits}x";
        var watch = Stopwatch.StartNew();

        string result = SpreadsheetFormula.Neutralise(text);

        watch.Stop();
        Assert.Equal("'" + text, result);
        Assert.True(watch.Elapsed < QuickCheckLimit, $"The check took {watch.Elapsed}.");
    }

    [Fact]
    public void A_long_signed_number_stays_as_it_is()
    {
        string digits = new('1', LongRunLength);
        string number = $"-{digits}.{digits}";

        Assert.Equal(number, SpreadsheetFormula.Neutralise(number));
    }
}
