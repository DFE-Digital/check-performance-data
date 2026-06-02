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
