using System.Text;
using DfE.CheckPerformanceData.Web.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Common;

public class JsonSchemaValidatorTests
{
    [Theory]
    [InlineData("""{ "$schema": "https://json-schema.org/draft/2020-12/schema", "type": "object" }""")]
    [InlineData("""{ "type": "object", "properties": { "name": { "type": "string" } } }""")]
    [InlineData("""{ "definitions": { "foo": { "type": "string" } } }""")]
    public void TryValidate_returns_true_for_json_schema(string content)
    {
        var result = JsonSchemaValidator.TryValidate(StreamFrom(content), out var error);

        Assert.True(result);
        Assert.Null(error);
    }

    [Fact]
    public void TryValidate_returns_false_for_invalid_json()
    {
        var result = JsonSchemaValidator.TryValidate(StreamFrom("not json at all"), out var error);

        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryValidate_returns_false_when_root_is_not_an_object()
    {
        var result = JsonSchemaValidator.TryValidate(StreamFrom("""[1, 2, 3]"""), out var error);

        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryValidate_returns_false_for_plain_json_object_without_schema_keywords()
    {
        var result = JsonSchemaValidator.TryValidate(StreamFrom("""{ "name": "Alice", "age": 30 }"""), out var error);

        Assert.False(result);
        Assert.NotNull(error);
    }

    private static Stream StreamFrom(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));
}
