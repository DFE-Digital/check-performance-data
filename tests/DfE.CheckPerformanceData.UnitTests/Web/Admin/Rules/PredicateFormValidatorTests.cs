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
}
