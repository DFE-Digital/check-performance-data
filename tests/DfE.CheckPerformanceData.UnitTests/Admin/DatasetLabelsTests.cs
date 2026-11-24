using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin;

public sealed class DatasetLabelsTests
{
    [Theory]
    [InlineData(ResultsFileTags.Ks4Main, "Main results (KS4_MAIN)")]
    [InlineData(ResultsFileTags.Ks4Revised, "Revised results (KS4_Revised)")]
    [InlineData(ResultsFileTags.Post16LateResults1, "Late results 1 (16to19_LR1)")]
    public void A_results_slot_shows_its_plain_name_and_its_tag(string slot, string expected)
        => Assert.Equal(expected, DatasetLabels.For(slot));
}
