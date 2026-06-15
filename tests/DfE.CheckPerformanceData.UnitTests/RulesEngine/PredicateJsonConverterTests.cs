using System.Text.Json;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// Schema-level tests for the predicate JSON shape. These pin the JSON the rule
/// authors will write so a refactor of the converter can't silently change the
/// vocabulary out from under them.
/// </summary>
public sealed class PredicateJsonConverterTests
{
    private static readonly JsonSerializerOptions Opts = RulesJson.Options;

    // --- "otherwise" terminal ---

    [Fact]
    public void Reads_OtherwiseFromStringLiteral()
    {
        var p = JsonSerializer.Deserialize<Predicate>("\"otherwise\"", Opts);
        Assert.IsType<Predicate.Otherwise>(p);
    }

    [Fact]
    public void Reads_OtherwiseCaseInsensitive()
    {
        var p = JsonSerializer.Deserialize<Predicate>("\"OTHERWISE\"", Opts);
        Assert.IsType<Predicate.Otherwise>(p);
    }

    [Fact]
    public void Rejects_UnknownStringLiteral()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Predicate>("\"always\"", Opts));
    }

    // --- field+op shapes ---

    [Fact]
    public void Reads_FieldEqWithStringLiteral()
    {
        var json = """{ "field": "checkingWindowType", "eq": "KS4June" }""";
        var p = JsonSerializer.Deserialize<Predicate>(json, Opts);
        var eq = Assert.IsType<Predicate.FieldEq>(p);
        Assert.Equal("checkingWindowType", eq.Field);
        Assert.Equal(new FieldValue.Str("KS4June"), eq.Value);
    }

    [Fact]
    public void Reads_FieldEqWithBoolLiteral()
    {
        var json = """{ "field": "isAddBack", "eq": true }""";
        var p = JsonSerializer.Deserialize<Predicate>(json, Opts);
        var eq = Assert.IsType<Predicate.FieldEq>(p);
        Assert.Equal(new FieldValue.Bool(true), eq.Value);
    }

    [Fact]
    public void Reads_FieldNeq()
    {
        var json = """{ "field": "checkingWindowType", "neq": "KS4June" }""";
        var p = JsonSerializer.Deserialize<Predicate>(json, Opts);
        var neq = Assert.IsType<Predicate.FieldNeq>(p);
        Assert.Equal("checkingWindowType", neq.Field);
    }

    [Fact]
    public void Reads_FieldInWithMixedLiterals()
    {
        // The Inclusion outcome uses numeric-looking codes ("402", "404", ...).
        // Both string and number JSON literals should parse; the validator coerces them later.
        var json = """{ "field": "inclusionFlag", "in": ["402", "404", 407] }""";
        var p = JsonSerializer.Deserialize<Predicate>(json, Opts);
        var inP = Assert.IsType<Predicate.FieldIn>(p);
        Assert.Equal("inclusionFlag", inP.Field);
        Assert.Equal(3, inP.Values.Count);
        Assert.Equal(new FieldValue.Str("402"), inP.Values[0]);
        Assert.Equal(new FieldValue.Str("404"), inP.Values[1]);
        Assert.Equal(new FieldValue.Num(407m), inP.Values[2]);
    }

    [Theory]
    [InlineData("lt",  CompareOp.Lt)]
    [InlineData("lte", CompareOp.Lte)]
    [InlineData("gt",  CompareOp.Gt)]
    [InlineData("gte", CompareOp.Gte)]
    public void Reads_FieldCompareForEachOperator(string opKey, CompareOp expected)
    {
        var json = $$"""{ "field": "dateOfRemoval", "{{opKey}}": "2025-01-16" }""";
        var p = JsonSerializer.Deserialize<Predicate>(json, Opts);
        var cmp = Assert.IsType<Predicate.FieldCompare>(p);
        Assert.Equal("dateOfRemoval", cmp.Field);
        Assert.Equal(expected, cmp.Op);
        Assert.Equal(new FieldValue.Str("2025-01-16"), cmp.Value); // validator later coerces -> Date
    }

    [Fact]
    public void Rejects_FieldShapeWithoutOperator()
    {
        var json = """{ "field": "checkingWindowType" }""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Predicate>(json, Opts));
    }

    // --- combinators ---

    [Fact]
    public void Reads_AllOfWithTwoChildren()
    {
        var json = """
            {
              "all": [
                { "field": "checkingWindowType",      "eq": "KS4June" },
                { "field": "dateOfRemoval", "gt": "2025-01-16" }
              ]
            }
            """;
        var p = JsonSerializer.Deserialize<Predicate>(json, Opts);
        var all = Assert.IsType<Predicate.AllOf>(p);
        Assert.Equal(2, all.Items.Count);
        Assert.IsType<Predicate.FieldEq>(all.Items[0]);
        Assert.IsType<Predicate.FieldCompare>(all.Items[1]);
    }

    [Fact]
    public void Reads_AnyOfWithNestedAllOf()
    {
        var json = """
            {
              "any": [
                { "all": [ { "field": "checkingWindowType", "eq": "KS4June" } ] },
                { "field": "isAddBack", "eq": true }
              ]
            }
            """;
        var p = JsonSerializer.Deserialize<Predicate>(json, Opts);
        var any = Assert.IsType<Predicate.AnyOf>(p);
        Assert.IsType<Predicate.AllOf>(any.Items[0]);
        Assert.IsType<Predicate.FieldEq>(any.Items[1]);
    }

    [Fact]
    public void Reads_NotWrappingFieldPredicate()
    {
        var json = """{ "not": { "field": "hasTerminalIllness", "eq": true } }""";
        var p = JsonSerializer.Deserialize<Predicate>(json, Opts);
        var not = Assert.IsType<Predicate.Not>(p);
        Assert.IsType<Predicate.FieldEq>(not.Inner);
    }

    // --- domain-specific predicates ---

    [Fact]
    public void Reads_IsKnownAndCertain()
    {
        var json = """{ "isKnownAndCertain": "dateOfArrivalInEngland" }""";
        var p = JsonSerializer.Deserialize<Predicate>(json, Opts);
        var iknown = Assert.IsType<Predicate.IsKnownAndCertain>(p);
        Assert.Equal("dateOfArrivalInEngland", iknown.Field);
    }

    [Fact]
    public void Reads_OfficialLanguageIs()
    {
        var json = """{ "officialLanguageIs": "English", "countryField": "countryOfOrigin" }""";
        var p = JsonSerializer.Deserialize<Predicate>(json, Opts);
        var lang = Assert.IsType<Predicate.OfficialLanguageIs>(p);
        Assert.Equal("English", lang.Language);
        Assert.Equal("countryOfOrigin", lang.CountryField);
    }

    [Fact]
    public void Rejects_OfficialLanguageIsWithoutCountryField()
    {
        var json = """{ "officialLanguageIs": "English" }""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Predicate>(json, Opts));
    }

    // --- shape rejections ---

    [Fact]
    public void Rejects_UnknownObjectShape()
    {
        var json = """{ "frobnicate": "wibble" }""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Predicate>(json, Opts));
    }

    [Fact]
    public void Rejects_AllOfWithNonArrayBody()
    {
        var json = """{ "all": "not-an-array" }""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Predicate>(json, Opts));
    }

    // --- round-trip ---

    [Fact]
    public void RoundTrips_AllOfTree()
    {
        var json = """
            {"all":[{"field":"checkingWindowType","eq":"KS4June"},{"field":"dateOfRemoval","gt":"2025-01-16"}]}
            """.Trim();
        var p = JsonSerializer.Deserialize<Predicate>(json, Opts)!;
        var roundTrip = JsonSerializer.Serialize(p, Opts);
        var p2 = JsonSerializer.Deserialize<Predicate>(roundTrip, Opts)!;
        Assert.Equal(p.GetType(), p2.GetType());
    }
}
