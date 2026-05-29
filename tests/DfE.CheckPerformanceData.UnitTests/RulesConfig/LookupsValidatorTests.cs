using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesConfig;

public sealed class LookupsValidatorTests
{
    private static Lookups Map(params (string code, string[] langs)[] rows) =>
        new(rows.ToDictionary(r => r.code, r => (IReadOnlyList<string>)r.langs));

    [Fact]
    public void Valid_map_passes()
    {
        var result = new LookupsValidator().Validate(Map(("GB", new[] { "English", "Welsh" })));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Empty_country_code_fails()
    {
        var result = new LookupsValidator().Validate(Map(("", new[] { "English" })));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty country code"));
    }

    [Fact]
    public void Country_with_no_languages_fails()
    {
        var result = new LookupsValidator().Validate(Map(("GB", Array.Empty<string>())));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'GB' has no languages"));
    }

    [Fact]
    public void Blank_language_fails()
    {
        var result = new LookupsValidator().Validate(Map(("GB", new[] { "English", "  " })));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'GB' has a blank language"));
    }
}
