using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using Xunit;

namespace DfE.CheckPerformanceData.UnitTests.CheckYourPupilData;

public sealed class PupilInclusionTests
{
    private static Dictionary<string, string> Row(params (string Key, string Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("True", true)]
    [InlineData("False", false)]
    [InlineData("not a bool", false)]
    public void TheIncludedStamp_DecidesInclusion(string stamp, bool included) =>
        Assert.Equal(included, PupilInclusion.IsIncluded(Row(("INCLUDED", stamp))));

    [Theory]
    [InlineData("401")]
    [InlineData("201")]
    [InlineData("501")]
    public void AnInclusionCode_WithNoStamp_IsNotIncluded(string pincl) =>
        Assert.False(PupilInclusion.IsIncluded(Row(("P_INCL", pincl))));
}
