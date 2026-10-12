namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// FR-006 / SC-004 scope guard (issue #515): the results-enquiry confirmation screen opens its
// "What happens next" section with the *same* phrase the amendment screen is being corrected
// away from — "After you submit your changes:". That screen's subject is an enquiry, not
// amendments, so it must stay exactly as it is today. This test is green before and after the
// change and fails only if a future over-eager search-and-replace spills onto it.
public sealed class EnquiryConfirmationViewSourceTests
{
    private static string ReadViewSource(string relativePath)
    {
        var viewsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Web"));
        return File.ReadAllText(Path.Combine(viewsDir, relativePath));
    }

    [Fact]
    public void results_enquiry_confirmation_copy_is_unchanged()
    {
        var source = ReadViewSource("Views/Journey/EnquiryConfirmation.cshtml");

        Assert.Contains("Results enquiry submitted", source);
        Assert.Contains("After you submit your changes:", source);
        Assert.Contains("the DfE will review your enquiry", source);
        Assert.Contains("your performance data will be updated in the Spring, if required", source);
    }
}
