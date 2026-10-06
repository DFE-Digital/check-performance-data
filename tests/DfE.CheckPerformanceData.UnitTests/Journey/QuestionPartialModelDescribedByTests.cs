using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Web.Controllers.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// _FileUpload.cshtml renders id="fileUpload-hint" only when the question has a hint, and
// id="fileUpload-error" only when there is an error. The "N of 6 files added." line
// (id="fileUpload-count") is always rendered beside the input and is therefore always named.
// DescribedBy must name exactly the ids
// that are on the page: an id that isn't rendered is a dangling aria-describedby reference,
// which resolves to nothing and silently drops the description for screen reader users.
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
    public void DescribedBy_WithHintAndNoError_NamesTheHintAndTheCount()
    {
        Assert.Equal("fileUpload-hint fileUpload-count", Build(hint: "Evidence must be in a PDF format").DescribedBy);
    }

    [Fact]
    public void DescribedBy_WithNoHintAndNoError_NamesOnlyTheCount()
    {
        // Previously returned "fileUpload-hint" unconditionally, pointing at an element the
        // view never rendered.
        Assert.Equal("fileUpload-count", Build(hint: null).DescribedBy);
    }

    [Fact]
    public void DescribedBy_WithNoHintButAnError_NamesTheCountAndTheError()
    {
        Assert.Equal("fileUpload-count fileUpload-error", Build(hint: null, error: "Select a file").DescribedBy);
    }

    [Fact]
    public void DescribedBy_WithHintAndError_NamesBothInReadingOrder()
    {
        Assert.Equal("fileUpload-hint fileUpload-count fileUpload-error",
            Build(hint: "Evidence must be in a PDF format", error: "Select a file").DescribedBy);
    }

    [Fact]
    public void DescribedBy_WithHintAndUploadError_NamesAllThree()
    {
        Assert.Equal("fileUpload-hint fileUpload-count fileUpload-error",
            Build(hint: "Evidence must be in a PDF format", uploadError: "The file must be a PDF").DescribedBy);
    }
}
