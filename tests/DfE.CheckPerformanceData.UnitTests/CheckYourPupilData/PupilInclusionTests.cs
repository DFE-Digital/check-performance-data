using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using Xunit;

namespace DfE.CheckPerformanceData.UnitTests.CheckYourPupilData;

public sealed class PupilInclusionTests
{
    private static Dictionary<string, string> Row(params (string Key, string Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("401", true)]
    [InlineData("431", true)]
    [InlineData("402", false)]
    [InlineData("", false)]
    [InlineData("not a code", false)]
    public void PIncl_DecidesInclusion_ByTheKs4Codes(string pincl, bool included) =>
        Assert.Equal(included, PupilInclusion.IsIncluded(Row(("P_INCL", pincl))));

    [Fact]
    public void ARowWithNoInclusionSignal_IsNotIncluded() =>
        Assert.False(PupilInclusion.IsIncluded(Row(("SURNAME", "Smith"))));

    [Theory]
    [InlineData("True", "402", true)]
    [InlineData("False", "401", false)]
    public void AnIncludedStamp_WinsOverPIncl(string stamp, string pincl, bool included) =>
        Assert.Equal(included, PupilInclusion.IsIncluded(Row(("INCLUDED", stamp), ("P_INCL", pincl))));

    [Theory]
    [InlineData(401, "Pupil on roll and included in key stage 4 (both NOR and results).")]
    [InlineData(402, "Pupil not on roll and omitted from all figures to be published.")]
    [InlineData(999, "")]
    [InlineData(null, "")]
    public void Ks4Description_NamesTheCode(int? pincl, string description) =>
        Assert.Equal(description, PupilInclusion.Ks4Description(pincl));
}
