using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// The runs history (AB#294590) shows four statuses where the pipeline has eight. This is the one
// mapping; a new EgressRunStatus that is not placed here must fail loudly, never fall into a bucket.
public sealed class EgressRunOutcomesTests
{
    [Theory]
    [InlineData(EgressRunStatus.Transferred, EgressRunOutcome.Success)]
    [InlineData(EgressRunStatus.PreprocessingFailed, EgressRunOutcome.Failed)]
    [InlineData(EgressRunStatus.TransferFailed, EgressRunOutcome.Failed)]
    [InlineData(EgressRunStatus.Pulled, EgressRunOutcome.Draft)]
    [InlineData(EgressRunStatus.Preprocessing, EgressRunOutcome.Draft)]
    [InlineData(EgressRunStatus.Preprocessed, EgressRunOutcome.Draft)]
    [InlineData(EgressRunStatus.Transferring, EgressRunOutcome.Draft)]
    [InlineData(EgressRunStatus.Abandoned, EgressRunOutcome.Abandoned)]
    public void Every_status_has_exactly_one_outcome(EgressRunStatus status, EgressRunOutcome expected)
        => Assert.Equal(expected, EgressRunOutcomes.Of(status));

    [Fact]
    public void Every_status_the_enum_declares_is_mapped()
    {
        foreach (var status in Enum.GetValues<EgressRunStatus>())
            Assert.Contains(EgressRunOutcomes.Of(status), EgressRunOutcomes.All);
    }

    [Fact]
    public void Statuses_of_each_outcome_are_the_exact_inverse_of_the_mapping()
    {
        foreach (var outcome in EgressRunOutcomes.All)
        {
            var expected = Enum.GetValues<EgressRunStatus>().Where(s => EgressRunOutcomes.Of(s) == outcome).ToList();
            Assert.Equal(expected, EgressRunOutcomes.StatusesOf(outcome));
        }
        Assert.Equal([EgressRunStatus.PreprocessingFailed, EgressRunStatus.TransferFailed], EgressRunOutcomes.StatusesOf(EgressRunOutcome.Failed));
    }

    [Fact]
    public void All_is_the_filters_option_order()
        => Assert.Equal([EgressRunOutcome.Success, EgressRunOutcome.Failed, EgressRunOutcome.Draft, EgressRunOutcome.Abandoned], EgressRunOutcomes.All);

    // FLAGGED copy (AB#294590).
    [Theory]
    [InlineData(EgressRunOutcome.Success, "Success")]
    [InlineData(EgressRunOutcome.Failed, "Failed")]
    [InlineData(EgressRunOutcome.Draft, "Draft")]
    [InlineData(EgressRunOutcome.Abandoned, "Abandoned")]
    public void Labels_are_the_tickets_words(EgressRunOutcome outcome, string expected)
        => Assert.Equal(expected, EgressRunOutcomes.Label(outcome));

    [Theory]
    [InlineData("success", EgressRunOutcome.Success)]
    [InlineData("Failed", EgressRunOutcome.Failed)]
    [InlineData("DRAFT", EgressRunOutcome.Draft)]
    [InlineData(" abandoned ", EgressRunOutcome.Abandoned)]
    public void A_query_value_parses_by_name_in_any_casing(string value, EgressRunOutcome expected)
        => Assert.Equal(expected, EgressRunOutcomes.TryParse(value));

    // A numeric string must not parse: "2" is not a status a user can ask for, and Enum.TryParse
    // would otherwise accept it.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]
    [InlineData("2")]
    [InlineData("Transferred")]
    public void Anything_else_is_no_filter(string? value)
        => Assert.Null(EgressRunOutcomes.TryParse(value));
}
