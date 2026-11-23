using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class PredicateFormValidatorTests
{
    [Fact]
    public void Empty_Composite_Is_Rejected()
    {
        var nodes = new List<PredicateNodeForm> { new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf } };
        var errors = PredicateFormValidator.Validate(nodes);
        Assert.Contains(errors, e => e.Contains("must contain at least one"));
    }

    [Fact]
    public void Not_With_Two_Children_Is_Rejected()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.Not },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.IsKnownAndCertain, Field = "checkingWindowType", Operator = "known" },
            new() { Id = 3, ParentId = 1, Kind = PredicateKind.IsKnownAndCertain, Field = "pupilAge", Operator = "known" },
        };
        Assert.Contains(PredicateFormValidator.Validate(nodes), e => e.Contains("exactly one"));
    }

    [Fact]
    public void Leaf_Without_Field_Is_Rejected()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "", Operator = "eq", Value = "x" },
        };
        Assert.Contains(PredicateFormValidator.Validate(nodes), e => e.Contains("needs a field"));
    }

    [Fact]
    public void Eq_Without_Value_Is_Rejected_But_Known_Is_Allowed()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "checkingWindowType", Operator = "eq", Value = "" },
            new() { Id = 3, ParentId = 1, Kind = PredicateKind.IsKnownAndCertain, Field = "pupilAge", Operator = "known" },
        };
        var errors = PredicateFormValidator.Validate(nodes);
        Assert.Contains(errors, e => e.Contains("needs a value"));
        Assert.Single(errors);
    }

    [Fact]
    public void FieldIn_With_No_Values_Is_Rejected()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldIn, Field = "inclusionFlag", Operator = "in" },
        };
        Assert.Contains(PredicateFormValidator.Validate(nodes), e => e.Contains("at least one value"));
    }

    [Fact]
    public void Empty_Node_List_Is_Rejected()
    {
        Assert.Contains(PredicateFormValidator.Validate(new List<PredicateNodeForm>()),
            e => e.Contains("at least one condition"));
    }

    [Fact]
    public void Well_Formed_Tree_Has_No_Errors()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "checkingWindowType", Operator = "eq", Value = "KS4June" },
        };
        Assert.Empty(PredicateFormValidator.Validate(nodes));
    }

    // #800: a window-relative date token is only meaningful on a Date field. A non-date
    // field would compare a plain string against a request that can never carry one, so
    // the rule would silently never fire. The validator must stop it before save.

    [Fact]
    public void RelativeDateToken_On_NonDateField_Is_Rejected()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "checkingWindowType", Operator = "eq", Value = "window:secondMondayOfMay" },
        };
        Assert.Contains(PredicateFormValidator.Validate(nodes), e => e.Contains("is not a date field"));
    }

    [Fact]
    public void RelativeDateToken_In_Values_On_NonDateField_Is_Rejected()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldIn, Field = "checkingWindowType", Operator = "in",
                Values = ["KS4June", "window:secondMondayOfMay"] },
        };
        Assert.Contains(PredicateFormValidator.Validate(nodes), e => e.Contains("is not a date field"));
    }

    [Fact]
    public void RelativeDateToken_On_DateField_Is_Allowed()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "dateOfRemoval", Operator = "eq", Value = "window:secondMondayOfMay" },
        };
        Assert.Empty(PredicateFormValidator.Validate(nodes));
    }

    // A token that starts like a window-relative date but matches no supported form must
    // fail validation with a message naming the forms, not silently save as a plain string.

    [Theory]
    [InlineData("window:secondMondayOfJanuary")]
    [InlineData("window:firstSeptemberMinusYears:")]
    [InlineData("window:firstSeptemberMinusYears:three")]
    [InlineData("window:dayMonthOfWindowYear:9")]
    [InlineData("window:dayMonthOfWindowYear:9-31-2027")]
    [InlineData("window:")]
    public void MalformedRelativeDateToken_On_DateField_Is_Rejected_WithSupportedForms(string value)
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "dateOfRemoval", Operator = "eq", Value = value },
        };
        var errors = PredicateFormValidator.Validate(nodes);
        Assert.Contains(errors, e => e.Contains("not a supported window-relative date")
            && e.Contains("window:secondMondayOfMay")
            && e.Contains("window:firstSeptemberMinusYears:<years>")
            && e.Contains("window:dayMonthOfWindowYear:<month>-<day>"));
    }

    [Fact]
    public void MalformedRelativeDateToken_In_Values_On_DateField_Is_Rejected()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldIn, Field = "dateOfRemoval", Operator = "in",
                Values = ["window:secondMondayOfMay", "window:dayMonthOfWindowYear:9-x"] },
        };
        // One valid token, one malformed: exactly one error, for the malformed value.
        var errors = PredicateFormValidator.Validate(nodes);
        Assert.Single(errors);
        Assert.Contains("window:dayMonthOfWindowYear:9-x", errors[0]);
        Assert.Contains("not a supported window-relative date", errors[0]);
    }
}
