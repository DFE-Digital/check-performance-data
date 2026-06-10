using DfE.CheckPerformanceData.Application.Settings;

namespace DfE.CheckPerformanceData.UnitTests.Settings;

public sealed class SettingDefinitionsTests
{
    [Theory]
    [InlineData("Dlq:FullPayloadEnabled", "false")]
    [InlineData("Dlq:AlertThreshold", "10")]
    [InlineData("Dlq:AlertRecipients", "")]
    [InlineData("Dlq:RetentionDays", "90")]
    public void All_ContainsDlqKey_WithExpectedDefault(string key, string expectedDefault)
    {
        var definition = SettingDefinitions.Find(key);

        Assert.NotNull(definition);
        Assert.Equal(expectedDefault, definition!.DefaultValue);
    }

    [Fact]
    public void All_KeysAreUnique()
    {
        var keys = SettingDefinitions.All.Select(d => d.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Find_UnknownKey_ReturnsNull()
    {
        Assert.Null(SettingDefinitions.Find("Dlq:NotARealKey"));
    }
}
