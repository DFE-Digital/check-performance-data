using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

/// <summary>
/// Tests for <see cref="CountryNames"/> — derives a display name from an ISO 3166-1 alpha-2 code
/// via the runtime's ICU data, with a graceful fallback to the raw code.
/// </summary>
public sealed class CountryNamesTests
{
    [Theory]
    [InlineData("GB", "United Kingdom")]
    [InlineData("FR", "France")]
    [InlineData("US", "United States")]
    public void DisplayName_resolves_known_codes(string code, string expected)
    {
        Assert.Equal(expected, CountryNames.DisplayName(code));
    }

    [Theory]
    [InlineData("ZZ")]      // not an assigned region
    [InlineData("GB-ENG")]  // subdivision, not a plain alpha-2 region code
    [InlineData("")]
    [InlineData("   ")]
    public void DisplayName_falls_back_to_input_for_unrecognised_codes(string code)
    {
        Assert.Equal(code, CountryNames.DisplayName(code));
    }
}
