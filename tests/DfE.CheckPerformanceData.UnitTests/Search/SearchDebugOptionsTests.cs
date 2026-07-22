using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Web.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// Contract test for SearchDebugOptions — the concrete behind ISearchDebugOptions.
// Gates whether the <!-- rank: N --> HTML comment renders on /search. Default:
// true in Development, false in Production; override with the Search:ShowDebug
// configuration key. Truth table (config × environment → expected):
//
//   Search:ShowDebug | environment    | ShowSearchDebug
//   ---------------- | -------------- | ---------------
//   null (unset)     | Development    | true
//   null (unset)     | Production     | false
//   "true"           | Development    | true
//   "true"           | Production     | true   (config wins)
//   "false"          | Development    | false  (config wins)
//   "false"          | Production     | false
//
// Six rows: three config states × two environments. Any drift in the
// SearchDebugOptions accessor fails at least one row.
public sealed class SearchDebugOptionsTests
{
    private static IConfiguration Config(string? searchShowDebug)
    {
        var values = new Dictionary<string, string?>();
        if (searchShowDebug is not null)
            values["Search:ShowDebug"] = searchShowDebug;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IHostEnvironment Env(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = environmentName;
        return env;
    }

    [Theory]
    [InlineData(null, "Development", true)]
    [InlineData(null, "Production", false)]
    [InlineData("true", "Development", true)]
    [InlineData("true", "Production", true)]
    [InlineData("false", "Development", false)]
    [InlineData("false", "Production", false)]
    public void ShowSearchDebug_TruthTable_ResolvesConfigOverEnvironment(
        string? showDebugConfig, string environmentName, bool expected)
    {
        var sut = new SearchDebugOptions(Config(showDebugConfig), Env(environmentName));

        Assert.Equal(expected, sut.ShowSearchDebug);
    }
}
