using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

// AB#296648: "the service works out whether the second late results file is available from data it
// already holds" — it is never told separately. This is the one place that derivation lives, so the
// interstitial and any future gating cannot disagree about it.
public sealed class LateResultsAvailabilityTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");
    private const string Laestab = "860/4070";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Availability_mirrors_whether_the_school_holds_any_LR2_result(bool holdsLr2)
    {
        var results = Substitute.For<IStudentResultsClient>();
        results.AnyForSourceAsync(WindowId, Laestab, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(holdsLr2);

        var actual = await new LateResultsAvailability(results)
            .IsSecondLateResultsAvailableAsync(WindowId, Laestab);

        Assert.Equal(holdsLr2, actual);
    }

    [Fact]
    public async Task It_asks_for_the_second_late_results_tag_specifically()
    {
        // The tag is the data contract with ingestion. Asking for the wrong one (LR1, Revised) would
        // show or hide the interstitial on the strength of an unrelated file having landed.
        var results = Substitute.For<IStudentResultsClient>();

        await new LateResultsAvailability(results).IsSecondLateResultsAvailableAsync(WindowId, Laestab);

        await results.Received(1).AnyForSourceAsync(
            WindowId,
            Laestab,
            Arg.Is<string>(tag => tag == "16to19_LR2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task It_passes_the_window_and_school_through_untouched()
    {
        // Normalising the laestab is the blob client's job; doing it here too would hide a
        // disagreement between the two.
        var results = Substitute.For<IStudentResultsClient>();
        var otherWindow = Guid.NewGuid();

        await new LateResultsAvailability(results).IsSecondLateResultsAvailableAsync(otherWindow, "9334290");

        await results.Received(1).AnyForSourceAsync(
            otherWindow, "9334290", ResultsFileTags.Post16LateResults2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_results_client()
    {
        var results = Substitute.For<IStudentResultsClient>();
        using var cts = new CancellationTokenSource();

        await new LateResultsAvailability(results)
            .IsSecondLateResultsAvailableAsync(WindowId, Laestab, cts.Token);

        await results.Received(1).AnyForSourceAsync(
            WindowId, Laestab, ResultsFileTags.Post16LateResults2, cts.Token);
    }
}
