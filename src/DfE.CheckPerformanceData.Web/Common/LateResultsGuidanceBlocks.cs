using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Common;

/// <summary>
/// The content blocks that hold the late results guidance of a results enquiry. Each window type
/// has its own blocks (<see cref="WindowScopedContentKey"/>), so each can have its own words. A
/// default is the text that a block starts with.
/// </summary>
public sealed record LateResultsGuidanceBlocks(string OptionKey, string SummaryKey, string BodyKey, string InsetKey)
{
    public const string OptionDefault =
        "Incorrect grade (once you have checked your second late results file in late November)";

    public const string SummaryDefault = "Have you checked your second late results file?";

    public const string BodyDefault =
        "<p class=\"govuk-body\">Nearly all incorrect grades are updated in the second late results file. " +
        "We will email you in late November to let you know it's available.</p>";

    public const string InsetDefault =
        "Before you report an incorrect grade, check the second late results (issued in November) to " +
        "make sure the grade is not included there.";

    private const string OptionKeyStem = "result-issue-incorrect-grade-option";
    private const string SummaryKeyStem = "result-issue-late-results-summary";
    private const string BodyKeyStem = "result-issue-late-results-body";
    private const string InsetKeyStem = "result-details-late-results-inset";

    public static LateResultsGuidanceBlocks For(CheckingWindowType windowType) => new(
        WindowScopedContentKey.For(OptionKeyStem, windowType),
        WindowScopedContentKey.For(SummaryKeyStem, windowType),
        WindowScopedContentKey.For(BodyKeyStem, windowType),
        WindowScopedContentKey.For(InsetKeyStem, windowType));
}
