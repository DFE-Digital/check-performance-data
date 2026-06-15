using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class LeafNormalizerTests
{
    [Fact]
    public void Compare_Token_Sets_Kind_And_Op()
    {
        var node = new PredicateNodeForm { Kind = PredicateKind.FieldEq, Field = "pupilAge", Operator = "gte" };
        LeafNormalizer.Normalize(node);
        Assert.Equal(PredicateKind.FieldCompare, node.Kind);
        Assert.Equal("Gte", node.Op);
    }

    [Fact]
    public void Eq_Token_Sets_FieldEq_And_Clears_Op()
    {
        var node = new PredicateNodeForm { Kind = PredicateKind.FieldCompare, Op = "Lt", Operator = "eq" };
        LeafNormalizer.Normalize(node);
        Assert.Equal(PredicateKind.FieldEq, node.Kind);
        Assert.Null(node.Op);
    }

    [Fact]
    public void Known_Token_Sets_IsKnownAndCertain()
    {
        var node = new PredicateNodeForm { Operator = "known" };
        LeafNormalizer.Normalize(node);
        Assert.Equal(PredicateKind.IsKnownAndCertain, node.Kind);
    }

    [Fact]
    public void Composite_Nodes_Are_Untouched()
    {
        var node = new PredicateNodeForm { Kind = PredicateKind.AnyOf };
        LeafNormalizer.Normalize(node);
        Assert.Equal(PredicateKind.AnyOf, node.Kind);
    }
}
