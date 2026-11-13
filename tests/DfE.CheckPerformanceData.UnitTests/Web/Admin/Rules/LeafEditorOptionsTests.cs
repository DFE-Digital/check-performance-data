using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class LeafEditorOptionsTests
{
    [Fact]
    public void Number_Field_Offers_Comparisons_Eq_And_Known()
    {
        var tokens = LeafEditorOptions.OperatorTokensFor("pupilAge").Select(o => o.Token).ToList();
        Assert.Equal(new[] { "lt", "lte", "gt", "gte", "eq", "known" }, tokens);
    }

    [Fact]
    public void String_Field_Offers_Eq_Neq_In_Known_Lang()
    {
        var tokens = LeafEditorOptions.OperatorTokensFor("checkingWindowType").Select(o => o.Token).ToList();
        Assert.Equal(new[] { "eq", "neq", "in", "known", "lang" }, tokens);
    }

    [Fact]
    public void Bool_Field_Offers_Eq_Neq_Known()
    {
        var tokens = LeafEditorOptions.OperatorTokensFor("isAddBack").Select(o => o.Token).ToList();
        Assert.Equal(new[] { "eq", "neq", "known" }, tokens);
    }

    [Fact]
    public void Unknown_Field_Returns_Empty()
    {
        Assert.Empty(LeafEditorOptions.OperatorTokensFor("doesNotExist"));
    }

    [Fact]
    public void ValueEditor_Reflects_Operator_And_Type()
    {
        Assert.Equal(ValueEditorKind.None, LeafEditorOptions.ValueEditor("checkingWindowType", "known"));
        Assert.Equal(ValueEditorKind.List, LeafEditorOptions.ValueEditor("checkingWindowType", "in"));
        Assert.Equal(ValueEditorKind.Language, LeafEditorOptions.ValueEditor("checkingWindowType", "lang"));
        Assert.Equal(ValueEditorKind.BoolSelect, LeafEditorOptions.ValueEditor("isAddBack", "eq"));
        Assert.Equal(ValueEditorKind.Number, LeafEditorOptions.ValueEditor("pupilAge", "gte"));
        Assert.Equal(ValueEditorKind.Date, LeafEditorOptions.ValueEditor("schoolAdmissionDate", "lt"));
        Assert.Equal(ValueEditorKind.Text, LeafEditorOptions.ValueEditor("checkingWindowType", "eq"));
    }

    // #800: a date field holding a window-relative date token gets the read-only
    // relative-date editor; a fixed ISO date keeps the date input.

    [Fact]
    public void ValueEditor_Picks_The_Relative_Date_Editor_For_A_Window_Token()
    {
        Assert.Equal(ValueEditorKind.RelativeDate,
            LeafEditorOptions.ValueEditor("dateOfRemoval", "eq", "window:secondMondayOfMay"));
        Assert.Equal(ValueEditorKind.RelativeDate,
            LeafEditorOptions.ValueEditor("schoolAdmissionDate", "lte", "window:firstSeptemberMinusYears:3"));
        Assert.Equal(ValueEditorKind.RelativeDate,
            LeafEditorOptions.ValueEditor("dateAddedToRoll", "gte", "window:dayMonthOfWindowYear:4-1"));
    }

    [Fact]
    public void ValueEditor_Keeps_A_Fixed_Date_On_The_Date_Editor()
    {
        Assert.Equal(ValueEditorKind.Date,
            LeafEditorOptions.ValueEditor("schoolAdmissionDate", "lt", "2025-09-01"));
    }

    [Fact]
    public void ValueEditor_Ignores_A_Window_Token_On_A_Non_Date_Field()
    {
        Assert.Equal(ValueEditorKind.Text,
            LeafEditorOptions.ValueEditor("checkingWindowType", "eq", "window:secondMondayOfMay"));
    }

    [Theory]
    [InlineData("window:secondMondayOfMay", true)]
    [InlineData("window:dayMonthOfWindowYear:4-1", true)]
    [InlineData("2025-09-01", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsRelativeDateToken_Recognises_Only_Window_Tokens(string? value, bool expected)
    {
        Assert.Equal(expected, PredicateForm.IsRelativeDateToken(value));
    }
}
