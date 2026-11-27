using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public class QualificationReferenceSetsTests
{
    [Fact]
    public void A_ks4_autumn_school_selects_from_the_ks4_list()
    {
        Assert.Equal(QualificationReferenceSet.Ks4, QualificationReferenceSets.ForSelection(CheckingWindowType.KS4Autumn));
    }

    [Theory]
    [InlineData(CheckingWindowType.Post16)]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS2)]
    [InlineData(null)]
    public void Each_other_window_selects_from_the_16_19_list(CheckingWindowType? windowType)
    {
        Assert.Equal(QualificationReferenceSet.Post16, QualificationReferenceSets.ForSelection(windowType));
    }

    [Fact]
    public void A_held_result_of_a_ks4_autumn_window_is_looked_up_in_the_ks4_list_and_then_the_16_19_list()
    {
        Assert.Equal(
            new[] { QualificationReferenceSet.Ks4, QualificationReferenceSet.Post16 },
            QualificationReferenceSets.ForHeldResult(CheckingWindowType.KS4Autumn));
    }

    [Theory]
    [InlineData(CheckingWindowType.Post16)]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS2)]
    [InlineData(null)]
    public void A_held_result_of_each_other_window_is_looked_up_in_the_16_19_list_only(CheckingWindowType? windowType)
    {
        Assert.Equal(new[] { QualificationReferenceSet.Post16 }, QualificationReferenceSets.ForHeldResult(windowType));
    }
}
