using DfE.CheckPerformanceData.Application.Dashboard;

namespace DfE.CheckPerformanceData.UnitTests.Dashboard;

public class LaestabNormaliserTests
{
    [Theory]
    [InlineData("933/4070", "9334070")]
    [InlineData("9334070", "9334070")]
    [InlineData(" 933/4070 ", "9334070")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("abc", "")]
    public void Normalise_ReturnsDigitsOnly(string? input, string expected)
        => Assert.Equal(expected, LaestabNormaliser.Normalise(input));
}
