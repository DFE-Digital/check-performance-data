using DfE.CheckPerformanceData.Application.WindowManagement.Extensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// AB#307773: the admin pastes the schools an extension names, one per line, in either form.
public sealed class LaestabListTests
{
    [Fact]
    public void Both_forms_parse_to_digits_once_each_in_input_order()
    {
        var result = LaestabList.Parse("933/4290\n9334290\n\n 860/4070 \n");

        Assert.Equal(["9334290", "8604070"], result.Laestabs);
        Assert.Empty(result.Errors);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Windows_line_endings_parse_as_lines()
    {
        var result = LaestabList.Parse("933/4290\r\n860/4070");

        Assert.Equal(["9334290", "8604070"], result.Laestabs);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Each_line_that_is_not_a_laestab_is_an_error_naming_its_line()
    {
        var result = LaestabList.Parse("93342901\nabc\n933-4290");

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "Line 1: '93342901' is not a LAESTAB. Enter 7 digits, as 933/4290 or 9334290.",
                "Line 2: 'abc' is not a LAESTAB. Enter 7 digits, as 933/4290 or 9334290.",
                "Line 3: '933-4290' is not a LAESTAB. Enter 7 digits, as 933/4290 or 9334290."
            ],
            result.Errors.Select(e => e.Message));
        Assert.Equal([1, 2, 3], result.Errors.Select(e => e.Line));
    }

    [Fact]
    public void Blank_lines_are_skipped_but_still_counted()
    {
        // The line number is the one the admin sees in the box, so a blank line above moves it.
        var result = LaestabList.Parse("933/4290\n\nabc");

        Assert.Equal(["9334290"], result.Laestabs);
        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.Line);
        Assert.Equal("abc", error.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n \n")]
    public void Nothing_entered_is_an_empty_list_with_no_errors(string? text)
    {
        var result = LaestabList.Parse(text);

        Assert.Empty(result.Laestabs);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void A_laestab_is_displayed_with_its_slash()
    {
        Assert.Equal("933/4290", LaestabList.Display("9334290"));
    }

    [Fact]
    public void A_list_is_displayed_one_laestab_per_line()
    {
        Assert.Equal("933/4290\n860/4070", LaestabList.DisplayLines(["9334290", "8604070"]));
    }
}
