using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Common;

public sealed class LateResultsGuidanceBlocksTests
{
    [Fact]
    public void A_ks4_autumn_window_has_its_own_blocks()
    {
        Assert.Equal(
            new LateResultsGuidanceBlocks(
                "result-issue-incorrect-grade-option-ks4autumn",
                "result-issue-late-results-summary-ks4autumn",
                "result-issue-late-results-body-ks4autumn",
                "result-details-late-results-inset-ks4autumn"),
            LateResultsGuidanceBlocks.For(CheckingWindowType.KS4Autumn));
    }

    [Fact]
    public void A_16_to_19_window_has_its_own_blocks()
    {
        Assert.Equal(
            new LateResultsGuidanceBlocks(
                "result-issue-incorrect-grade-option-post16",
                "result-issue-late-results-summary-post16",
                "result-issue-late-results-body-post16",
                "result-details-late-results-inset-post16"),
            LateResultsGuidanceBlocks.For(CheckingWindowType.Post16));
    }

    [Fact]
    public void No_two_blocks_share_a_key()
    {
        var keys = Enum.GetValues<CheckingWindowType>()
            .Select(LateResultsGuidanceBlocks.For)
            .SelectMany(b => new[] { b.OptionKey, b.SummaryKey, b.BodyKey, b.InsetKey })
            .ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_block_starts_with_the_words_a_school_read_before_the_blocks()
    {
        Assert.Equal(
            "Incorrect grade (once you have checked your second late results file in late November)",
            LateResultsGuidanceBlocks.OptionDefault);
        Assert.Equal("Have you checked your second late results file?", LateResultsGuidanceBlocks.SummaryDefault);
        Assert.Equal(
            "<p class=\"govuk-body\">Nearly all incorrect grades are updated in the second late results file. " +
            "We will email you in late November to let you know it's available.</p>",
            LateResultsGuidanceBlocks.BodyDefault);
        Assert.Equal(
            "Before you report an incorrect grade, check the second late results (issued in November) to " +
            "make sure the grade is not included there.",
            LateResultsGuidanceBlocks.InsetDefault);
    }
}
