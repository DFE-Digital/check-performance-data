using DfE.CheckPerformanceData.Application.ContentStaging;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

public sealed class ContentStagingJsonTests
{
    // Pin the MaxDepth guarantee so a future refactor of the shared options object can't
    // silently open the door to a nested-JSON DoS payload. Bundle nesting is 3–4 levels
    // (bundle → pages → versions → primitives) so 32 is ample headroom.
    [Fact]
    public void Options_MaxDepth_IsExplicitlyBoundedFarBelowTheStjDefault()
    {
        Assert.Equal(32, ContentStagingJson.Options.MaxDepth);
    }

    // Round-trip evidence the cap does its job: a payload with more than MaxDepth of
    // nesting should throw at deserialisation rather than blowing the stack or allocating
    // deep parser state. Uses raw JSON so the shape doesn't need a matching CLR type.
    [Fact]
    public void Deserialize_PayloadNestedPastMaxDepth_Throws()
    {
        var deepJson = new string('[', 33) + new string(']', 33);
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<object>(deepJson, ContentStagingJson.Options));
    }
}
