using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class PredicateDescriberTests
{
    private static PredicateNode Describe(Predicate p) => PredicateDescriber.Describe(p);

    [Fact]
    public void FieldEq_string_renders_field_is_value()
    {
        var node = Describe(new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4")));
        Assert.True(node.IsLeaf);
        Assert.Equal("keyStage is \"KS4\"", node.Text);
    }

    [Fact]
    public void FieldNeq_bool_renders_is_not()
    {
        var node = Describe(new Predicate.FieldNeq("isAddBack", new FieldValue.Bool(true)));
        Assert.Equal("isAddBack is not true", node.Text);
    }

    [Fact]
    public void FieldIn_lists_values()
    {
        var node = Describe(new Predicate.FieldIn("inclusionFlag",
            new FieldValue[] { new FieldValue.Str("A"), new FieldValue.Str("B") }));
        Assert.Equal("inclusionFlag is one of: \"A\", \"B\"", node.Text);
    }

    [Fact]
    public void FieldCompare_renders_operator_phrase()
    {
        var node = Describe(new Predicate.FieldCompare("pupilAge", CompareOp.Gte, new FieldValue.Num(16m)));
        Assert.Equal("pupilAge is greater than or equal to 16", node.Text);
    }

    [Fact]
    public void IsKnownAndCertain_renders()
    {
        var node = Describe(new Predicate.IsKnownAndCertain("countryOfOrigin"));
        Assert.Equal("countryOfOrigin is known and certain", node.Text);
    }

    [Fact]
    public void OfficialLanguageIs_renders()
    {
        var node = Describe(new Predicate.OfficialLanguageIs("countryOfOrigin", "English"));
        Assert.Equal("\"English\" is an official language of the country in countryOfOrigin", node.Text);
    }

    [Fact]
    public void Otherwise_renders_catch_all()
    {
        var node = Describe(Predicate.Otherwise.Instance);
        Assert.Equal("Otherwise (always matches)", node.Text);
    }

    [Fact]
    public void Date_value_renders_iso()
    {
        var node = Describe(new Predicate.FieldEq("schoolAdmissionDate",
            new FieldValue.Date(new DateOnly(2025, 9, 1))));
        Assert.Equal("schoolAdmissionDate is 2025-09-01", node.Text);
    }

    [Fact]
    public void Uncertain_value_is_annotated()
    {
        var node = Describe(new Predicate.FieldEq("firstLanguage",
            new FieldValue.Uncertain(new FieldValue.Str("French"))));
        Assert.Equal("firstLanguage is \"French\" (uncertain)", node.Text);
    }

    [Fact]
    public void Unknown_value_renders_unknown()
    {
        var node = Describe(new Predicate.FieldEq("firstLanguage", FieldValue.Unknown.Instance));
        Assert.Equal("firstLanguage is unknown", node.Text);
    }

    [Fact]
    public void AllOf_has_header_and_children()
    {
        var node = Describe(new Predicate.AllOf(new Predicate[]
        {
            new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4")),
            new Predicate.IsKnownAndCertain("pupilAge")
        }));

        Assert.False(node.IsLeaf);
        Assert.Equal("All of the following are true:", node.Text);
        Assert.Equal(2, node.Children.Count);
        Assert.Equal("keyStage is \"KS4\"", node.Children[0].Text);
        Assert.Equal("pupilAge is known and certain", node.Children[1].Text);
    }

    [Fact]
    public void AnyOf_has_header()
    {
        var node = Describe(new Predicate.AnyOf(new Predicate[]
        {
            new Predicate.FieldEq("keyStage", new FieldValue.Str("KS2"))
        }));
        Assert.Equal("Any of the following are true:", node.Text);
        Assert.Single(node.Children);
    }

    [Fact]
    public void Not_wraps_single_child()
    {
        var node = Describe(new Predicate.Not(new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))));
        Assert.Equal("Not true:", node.Text);
        Assert.Single(node.Children);
        Assert.Equal("keyStage is \"KS4\"", node.Children[0].Text);
    }

    [Fact]
    public void Nested_composites_describe_recursively()
    {
        var node = Describe(new Predicate.AllOf(new Predicate[]
        {
            new Predicate.AnyOf(new Predicate[]
            {
                new Predicate.FieldEq("keyStage", new FieldValue.Str("KS2")),
                new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))
            }),
            new Predicate.Not(new Predicate.IsKnownAndCertain("pupilAge"))
        }));

        Assert.Equal("All of the following are true:", node.Text);
        Assert.Equal(2, node.Children.Count);
        Assert.Equal("Any of the following are true:", node.Children[0].Text);
        Assert.Equal(2, node.Children[0].Children.Count);
        Assert.Equal("Not true:", node.Children[1].Text);
    }
}
