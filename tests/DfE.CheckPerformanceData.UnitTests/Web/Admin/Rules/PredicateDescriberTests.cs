using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class PredicateDescriberTests
{
    private static PredicateNode Describe(Predicate p) => PredicateDescriber.Describe(p);

    [Fact]
    public void FieldEq_string_renders_field_is_value()
    {
        var node = Describe(new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June")));
        Assert.True(node.IsLeaf);
        Assert.Equal("checkingWindowType is \"KS4June\"", node.Text);
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
            new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June")),
            new Predicate.IsKnownAndCertain("pupilAge")
        }));

        Assert.False(node.IsLeaf);
        Assert.Equal("All of the following are true:", node.Text);
        Assert.Equal(2, node.Children.Count);
        Assert.Equal("checkingWindowType is \"KS4June\"", node.Children[0].Text);
        Assert.Equal("pupilAge is known and certain", node.Children[1].Text);
    }

    [Fact]
    public void AnyOf_has_header()
    {
        var node = Describe(new Predicate.AnyOf(new Predicate[]
        {
            new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS2"))
        }));
        Assert.Equal("Any of the following are true:", node.Text);
        Assert.Single(node.Children);
    }

    [Fact]
    public void Not_wraps_single_child()
    {
        var node = Describe(new Predicate.Not(new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June"))));
        Assert.Equal("Not true:", node.Text);
        Assert.Single(node.Children);
        Assert.Equal("checkingWindowType is \"KS4June\"", node.Children[0].Text);
    }

    [Fact]
    public void Nested_composites_describe_recursively()
    {
        var node = Describe(new Predicate.AllOf(new Predicate[]
        {
            new Predicate.AnyOf(new Predicate[]
            {
                new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS2")),
                new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June"))
            }),
            new Predicate.Not(new Predicate.IsKnownAndCertain("pupilAge"))
        }));

        Assert.Equal("All of the following are true:", node.Text);
        Assert.Equal(2, node.Children.Count);
        Assert.Equal("Any of the following are true:", node.Children[0].Text);
        Assert.Equal(2, node.Children[0].Children.Count);
        Assert.Equal("Not true:", node.Children[1].Text);
    }

    // #800: the window-aware overload resolves a window-relative date literal to the
    // concrete date for the window being viewed.

    private static PredicateNode DescribeForWindow(Predicate p, DateOnly windowStart) =>
        PredicateDescriber.Describe(p, windowStart);

    [Fact]
    public void SecondMondayOfMay_resolves_to_the_concrete_date_for_the_window()
    {
        var node = DescribeForWindow(
            new Predicate.FieldEq("dateOfRemoval",
                new FieldValue.RelativeDate(new RelativeDateSpec.SecondMondayOfMay())),
            new DateOnly(2027, 1, 4));
        Assert.Equal("dateOfRemoval is 10 May 2027 (window-relative: second Monday of May)", node.Text);
    }

    [Fact]
    public void FirstSeptemberMinusYears_resolves_years_back_from_the_window_year()
    {
        var node = DescribeForWindow(
            new Predicate.FieldCompare("schoolAdmissionDate", CompareOp.Lte,
                new FieldValue.RelativeDate(new RelativeDateSpec.FirstSeptemberMinusYears(3))),
            new DateOnly(2027, 1, 4));
        Assert.Equal("schoolAdmissionDate is less than or equal to 1 September 2024 (window-relative: 1 September, 3 years before the window year)", node.Text);
    }

    [Fact]
    public void DayMonthOfWindowYear_resolves_the_named_day_of_the_window_year()
    {
        var node = DescribeForWindow(
            new Predicate.FieldEq("dateAddedToRoll",
                new FieldValue.RelativeDate(new RelativeDateSpec.DayMonthOfWindowYear(4, 1))),
            new DateOnly(2027, 1, 4));
        Assert.Equal("dateAddedToRoll is 1 April 2027 (window-relative: 1 April of the window year)", node.Text);
    }

    [Fact]
    public void A_relative_date_that_cannot_resolve_shows_the_form_name_with_no_date()
    {
        // 29 February has no date in 2027 — the describer shows the form name and no
        // date, never a guessed one.
        var node = DescribeForWindow(
            new Predicate.FieldEq("dateOfRemoval",
                new FieldValue.RelativeDate(new RelativeDateSpec.DayMonthOfWindowYear(2, 29))),
            new DateOnly(2027, 1, 4));
        Assert.Equal("dateOfRemoval is 29 February of the window year", node.Text);
    }

    [Fact]
    public void A_fixed_date_renders_unchanged_with_and_without_a_window()
    {
        var leaf = new Predicate.FieldEq("schoolAdmissionDate", new FieldValue.Date(new DateOnly(2025, 9, 1)));
        Assert.Equal("schoolAdmissionDate is 2025-09-01", DescribeForWindow(leaf, new DateOnly(2027, 1, 4)).Text);
        Assert.Equal("schoolAdmissionDate is 2025-09-01", Describe(leaf).Text);
    }

    [Fact]
    public void The_single_argument_member_delegates_without_a_window()
    {
        // No window start date means a relative literal shows its form name, never "unknown".
        var node = Describe(new Predicate.FieldEq("dateOfRemoval",
            new FieldValue.RelativeDate(new RelativeDateSpec.SecondMondayOfMay())));
        Assert.Equal("dateOfRemoval is second Monday of May", node.Text);
    }

    [Fact]
    public void A_relative_date_in_a_nested_group_resolves_for_the_window()
    {
        var node = DescribeForWindow(new Predicate.AllOf(new Predicate[]
        {
            new Predicate.Not(new Predicate.FieldCompare("dateOfRemoval", CompareOp.Lt,
                new FieldValue.RelativeDate(new RelativeDateSpec.SecondMondayOfMay())))
        }), new DateOnly(2027, 1, 4));

        var not = node.Children[0];
        Assert.Equal("Not true:", not.Text);
        Assert.Equal(
            "dateOfRemoval is less than 10 May 2027 (window-relative: second Monday of May)",
            not.Children[0].Text);
    }
}
