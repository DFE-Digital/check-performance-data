using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.Application.UnitTests.ZendeskClient;

/// <summary>
/// Exercises the real <see cref="ZendeskTicketFieldOptions"/> option maps directly
/// (the ticket-composition tests stub <see cref="IZendeskTicketFieldService"/>,
/// so they cannot catch a missing option here).
/// </summary>
public sealed class ZendeskTicketFieldOptionsTests
{
    [Theory]
    [InlineData("KS2", "ks2")]
    [InlineData("KS4June", "ks4")]
    [InlineData("KS4Autumn", "ks4")]
    [InlineData("ks4", "ks4")]
    public void KeyStage_GetOptionValue_MapsCheckingWindowTypes(string windowType, string expected)
    {
        var value = ZendeskTicketFieldOptions.GetOptionValue(ZendeskTicketFieldConstants.KeyStageName, windowType);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void KeyStage_GetOptionValue_ReturnsNullForUnmappedWindowType()
    {
        var value = ZendeskTicketFieldOptions.GetOptionValue(ZendeskTicketFieldConstants.KeyStageName, "Post16");

        Assert.Null(value);
    }
}
