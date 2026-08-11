namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Analytics;

using DfE.CheckPerformanceData.Web.Analytics;

public sealed class QueryRedactionTests
{
    [Theory]
    [InlineData("includedSearch=John+Smith", "includedSearch=%5Bredacted%5D")]
    [InlineData("nonIncludedSearch=Jane&activeTab=included", "nonIncludedSearch=%5Bredacted%5D&activeTab=included")]
    [InlineData("query=smi", "query=%5Bredacted%5D")]
    [InlineData("INCLUDEDSEARCH=x", "INCLUDEDSEARCH=%5Bredacted%5D")]
    public void Redact_masks_denylisted_param_values(string input, string expected)
        => Assert.Equal(expected, QueryRedaction.Redact(input));

    [Theory]
    [InlineData("q=pupil+premium")]                 // site search term is intentionally kept (R21)
    [InlineData("includedPage=2&activeTab=included")]
    public void Redact_leaves_other_params_untouched(string input)
        => Assert.Equal(input, QueryRedaction.Redact(input));

    [Fact]
    public void Redact_keeps_empty_values_empty()
        => Assert.Equal("includedSearch=&activeTab=included",
            QueryRedaction.Redact("includedSearch=&activeTab=included"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_passes_through_null_and_empty(string? input)
        => Assert.Equal(input, QueryRedaction.Redact(input));
}
