using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesConfig;

public sealed class LookupsValidatorTests
{
    private readonly LookupsValidator _sut = new();

    private static Lookups Map(params (string code, string[] langs)[] rows) =>
        new(rows.ToDictionary(r => r.code, r => (IReadOnlyList<string>)r.langs));

    [Fact]
    public void Valid_map_passes()
    {
        var result = _sut.Validate(Map(("GB", new[] { "English", "Welsh" })));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Empty_country_code_fails()
    {
        var result = _sut.Validate(Map(("", new[] { "English" })));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty country code"));
    }

    [Fact]
    public void Country_with_no_languages_fails()
    {
        var result = _sut.Validate(Map(("GB", Array.Empty<string>())));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'GB' has no languages"));
    }

    [Fact]
    public void Blank_language_fails()
    {
        var result = _sut.Validate(Map(("GB", new[] { "English", "  " })));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'GB' has a blank language"));
    }

    [Fact]
    public void Multiple_failing_countries_accumulate_all_errors()
    {
        var result = _sut.Validate(Map(
            ("GB", new[] { "English" }),          // valid — no error
            ("", new[] { "French" }),             // empty code
            ("DE", Array.Empty<string>()),        // no languages
            ("FR", new[] { "French", " " })));    // blank language

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Contains("empty country code"));
        Assert.Contains(result.Errors, e => e.Contains("'DE' has no languages"));
        Assert.Contains(result.Errors, e => e.Contains("'FR' has a blank language"));
    }
}
