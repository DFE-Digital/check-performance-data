using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Web.Controllers.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// _FileUpload.cshtml renders the "N of 6 files added." line (id="fileUpload-count") in the
// same branch as the file input, so it is always named, and id="fileUpload-error" only when
// there is an error. DescribedBy must name exactly the ids that are on the page: an id that
// isn't rendered is a dangling aria-describedby reference, which resolves to nothing and
// silently drops the whole description for screen reader users.
//
// The question's hint is rendered — as visible text under the question heading — but is
// deliberately NOT named (#389). Chrome exposes a native file input as a button, and JAWS
// does not read a button's description aloud: it directs the user to press a key to hear
// "descriptive text", which was the hint they could already see. See docs/accessibility.md.
public class QuestionPartialModelDescribedByTests
{
    private static QuestionPartialModel Build(string? hint, string? error = null, string? uploadError = null) =>
        new()
        {
            PageId = "evidence",
            Question = new Question { Id = "evidence", Type = QuestionType.FileUpload, Title = "Upload files", Hint = hint },
            Error = error,
            UploadError = uploadError
        };

    [Fact]
    public void DescribedBy_WithHintAndNoError_NamesOnlyTheCount()
    {
        // #389: the hint is on the page for everyone to read; it is not the input's description.
        Assert.Equal("fileUpload-count", Build(hint: "Evidence must be in a PDF format").DescribedBy);
    }

    [Fact]
    public void DescribedBy_WithNoHintAndNoError_NamesOnlyTheCount()
    {
        Assert.Equal("fileUpload-count", Build(hint: null).DescribedBy);
    }

    [Fact]
    public void DescribedBy_WithNoHintButAnError_NamesTheCountAndTheError()
    {
        Assert.Equal("fileUpload-count fileUpload-error", Build(hint: null, error: "Select a file").DescribedBy);
    }

    [Fact]
    public void DescribedBy_WithHintAndError_NamesTheCountAndTheErrorInReadingOrder()
    {
        Assert.Equal("fileUpload-count fileUpload-error",
            Build(hint: "Evidence must be in a PDF format", error: "Select a file").DescribedBy);
    }

    [Fact]
    public void DescribedBy_WithHintAndUploadError_NamesTheCountAndTheError()
    {
        Assert.Equal("fileUpload-count fileUpload-error",
            Build(hint: "Evidence must be in a PDF format", uploadError: "The file must be a PDF").DescribedBy);
    }

    [Fact]
    public void DescribedBy_NeverNamesTheHint()
    {
        // #389, belt and braces over every combination: no state of the model may bring the
        // hint back into the description without reopening the ticket.
        var hint = "Evidence must be in a PDF format";
        foreach (var model in new[]
                 {
                     Build(hint),
                     Build(hint, error: "Select a file"),
                     Build(hint, uploadError: "The file must be a PDF"),
                     Build(hint, error: "Select a file", uploadError: "The file must be a PDF")
                 })
        {
            Assert.DoesNotContain("fileUpload-hint", model.DescribedBy);
        }
    }
}
