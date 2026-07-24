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

    [Fact]
    public void FullPayloadEnabled_IsBoolKind()
    {
        var definition = SettingDefinitions.Find(SettingKeys.DlqFullPayloadEnabled);

        Assert.NotNull(definition);
        Assert.Equal(SettingKind.Bool, definition!.Kind);
    }

    [Theory]
    [InlineData("Dlq:AlertThreshold", SettingKind.Int)]
    [InlineData("Dlq:RetentionDays", SettingKind.Int)]
    [InlineData("CMS:PageLength", SettingKind.Int)]
    [InlineData("Dlq:AlertRecipients", SettingKind.String)]
    public void NonBoolSettings_HaveExpectedKind(string key, SettingKind expected)
    {
        var definition = SettingDefinitions.Find(key);

        Assert.NotNull(definition);
        Assert.Equal(expected, definition!.Kind);
    }

    [Fact]
    public void SearchDebugOn_IsRegistered_AsBool_OffByDefault()
    {
        var definition = SettingDefinitions.Find(SettingKeys.SearchDebugOn);

        Assert.NotNull(definition);
        Assert.Equal("CMS:SearchDebugOn", definition!.Key);
        Assert.Equal(SettingKind.Bool, definition.Kind);
        Assert.Equal("false", definition.DefaultValue);
    }
}
