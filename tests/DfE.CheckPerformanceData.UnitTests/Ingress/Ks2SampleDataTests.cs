using DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;
using Newtonsoft.Json.Linq;

namespace DfE.CheckPerformanceData.UnitTests.Ingress;

// The KS2 summary seed writes every column of the summary schemas from one sample row, and stops
// when a column has no value. A column that a schema adds and the sample forgets fails here first.
public sealed class Ks2SampleDataTests
{
    [Theory]
    [InlineData("summary_schema.json")]
    [InlineData("summary-december_schema.json")]
    public void The_summary_row_has_a_value_for_every_column_of_the_schema(string file)
    {
        var schema = JObject.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Data", "Ingress", "ks2", file)));
        var columns = ((JObject)schema["properties"]!).Properties().Select(p => p.Name);

        var row = Ks2SampleData.Summary("123456", "9334290", 0, suppressDisadvantaged: false);

        Assert.Empty(columns.Where(c => !row.ContainsKey(c)));
    }
}
