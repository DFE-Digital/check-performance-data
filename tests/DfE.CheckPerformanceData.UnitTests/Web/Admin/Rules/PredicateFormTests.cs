using System.Text.Json;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class PredicateFormTests
{
    private static string Json(Predicate p) => JsonSerializer.Serialize(p, RulesJson.Options);

    private static Predicate Sample() => new Predicate.AllOf(new Predicate[]
    {
        new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4")),
        new Predicate.AnyOf(new Predicate[]
        {
            new Predicate.FieldCompare("pupilAge", CompareOp.Gte, new FieldValue.Num(16)),
            new Predicate.Not(new Predicate.FieldEq("isAddBack", new FieldValue.Bool(true))),
        }),
        new Predicate.FieldIn("inclusionFlag", new FieldValue[] { new FieldValue.Str("A"), new FieldValue.Str("B") }),
        new Predicate.FieldCompare("schoolAdmissionDate", CompareOp.Lt, new FieldValue.Date(new DateOnly(2025, 9, 1))),
        new Predicate.IsKnownAndCertain("firstLanguage"),
        new Predicate.OfficialLanguageIs("countryOfOrigin", "English"),
    });

    [Fact]
    public void Flatten_Then_Rebuild_Round_Trips()
    {
        var original = Sample();
        var flat = PredicateForm.Flatten(original);
        var rebuilt = PredicateForm.RebuildPredicate(flat);
        Assert.Equal(Json(original), Json(rebuilt));
    }

    [Fact]
    public void Flatten_Produces_DepthFirst_Parent_Before_Children()
    {
        var flat = PredicateForm.Flatten(new Predicate.AllOf(new Predicate[]
        {
            new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))
        }));

        Assert.Equal(2, flat.Count);
        Assert.Null(flat[0].ParentId);
        Assert.Equal(PredicateKind.AllOf, flat[0].Kind);
        Assert.Equal(flat[0].Id, flat[1].ParentId);
    }

    [Fact]
    public void Otherwise_Round_Trips()
    {
        var flat = PredicateForm.Flatten(Predicate.Otherwise.Instance);
        Assert.Equal(Json(Predicate.Otherwise.Instance), Json(PredicateForm.RebuildPredicate(flat)));
    }

    [Fact]
    public void FlattenForEditing_Wraps_Bare_Leaf_In_AllOf_Root()
    {
        var leaf = new Predicate.FieldIn("inclusionFlag",
            new FieldValue[] { new FieldValue.Str("402"), new FieldValue.Str("404") });

        var flat = PredicateForm.FlattenForEditing(leaf);

        Assert.Equal(2, flat.Count);
        var root = flat.Single(n => n.ParentId is null);
        Assert.Equal(PredicateKind.AllOf, root.Kind);
        var child = flat.Single(n => n.ParentId == root.Id);
        Assert.Equal(PredicateKind.FieldIn, child.Kind);
        Assert.Equal("inclusionFlag", child.Field);
    }

    [Fact]
    public void FlattenForEditing_Leaves_Existing_Group_Root_Untouched()
    {
        var group = new Predicate.AnyOf(new Predicate[]
        {
            new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4")),
            new Predicate.FieldEq("keyStage", new FieldValue.Str("KS2")),
        });

        var viaEditing = PredicateForm.FlattenForEditing(group);
        var viaFlatten = PredicateForm.Flatten(group);

        Assert.Equal(viaFlatten.Count, viaEditing.Count);
        Assert.Equal(PredicateKind.AnyOf, viaEditing.Single(n => n.ParentId is null).Kind);
    }

    [Theory]
    [InlineData(PredicateKind.AllOf)]
    [InlineData(PredicateKind.AnyOf)]
    public void RebuildPredicate_Collapses_SingleChild_Group_Root(PredicateKind rootKind)
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = rootKind },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "KS4" },
        };

        var rebuilt = PredicateForm.RebuildPredicate(nodes);

        Assert.IsType<Predicate.FieldEq>(rebuilt);
    }

    [Fact]
    public void FlattenForEditing_Then_Rebuild_RoundTrips_Bare_Leaf()
    {
        var leaf = new Predicate.FieldIn("inclusionFlag",
            new FieldValue[] { new FieldValue.Str("402"), new FieldValue.Str("404") });

        var rebuilt = PredicateForm.RebuildPredicate(PredicateForm.FlattenForEditing(leaf));

        Assert.Equal(Json(leaf), Json(rebuilt));
    }

    [Fact]
    public void RebuildPredicate_Does_Not_Collapse_MultiChild_Group_Root()
    {
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "KS4" },
            new() { Id = 3, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "KS2" },
        };

        var rebuilt = PredicateForm.RebuildPredicate(nodes);

        Assert.IsType<Predicate.AllOf>(rebuilt);
    }

    [Fact]
    public void RebuildPredicate_Preserves_NonRoot_SingleChild_Group()
    {
        // any[ all[ leaf ] ] — only the ROOT collapse rule applies, so the inner
        // single-child AllOf must survive (root AnyOf has a single child and collapses to it).
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AnyOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.AllOf },
            new() { Id = 3, ParentId = 2, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "KS4" },
        };

        var rebuilt = PredicateForm.RebuildPredicate(nodes);

        // Root AnyOf{single} collapses to its child, the inner AllOf{single}, which is preserved.
        var inner = Assert.IsType<Predicate.AllOf>(rebuilt);
        Assert.Single(inner.Items);
        Assert.IsType<Predicate.FieldEq>(inner.Items[0]);
    }

    [Theory]
    [InlineData(CompareOp.Lt, "lt")]
    [InlineData(CompareOp.Lte, "lte")]
    [InlineData(CompareOp.Gt, "gt")]
    [InlineData(CompareOp.Gte, "gte")]
    public void OperatorToken_Maps_Compare_Ops(CompareOp op, string expected)
    {
        Assert.Equal(expected, PredicateForm.OperatorToken(PredicateKind.FieldCompare, op.ToString()));
    }

    [Theory]
    [InlineData(PredicateKind.FieldEq, "eq")]
    [InlineData(PredicateKind.FieldNeq, "neq")]
    [InlineData(PredicateKind.FieldIn, "in")]
    [InlineData(PredicateKind.IsKnownAndCertain, "known")]
    [InlineData(PredicateKind.OfficialLanguageIs, "lang")]
    [InlineData(PredicateKind.Otherwise, "")]
    public void OperatorToken_Maps_Leaf_Kinds(PredicateKind kind, string expected)
    {
        Assert.Equal(expected, PredicateForm.OperatorToken(kind, null));
    }
}
