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
}
