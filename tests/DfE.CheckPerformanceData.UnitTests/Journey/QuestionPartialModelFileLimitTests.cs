using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Web.Controllers.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#304900: the upload takes six files. The page shows the limit from the same constant the
// rule uses, and reaching it must NOT hide the upload control — the ticket asks that a seventh
// file is rejected with a message, so the school has to be able to try. AtLimit is the
// (switched-off) page limit's flag and stays that way.
public class QuestionPartialModelFileLimitTests
{
    private static QuestionPartialModel WithFiles(int count, int maxEvidencePages = 0) =>
        new()
        {
            PageId = "evidence",
            MaxEvidencePages = maxEvidencePages,
            Question = new Question { Id = "evidence", Type = QuestionType.FileUpload, Title = "Upload files" },
            ExistingAnswer = new QuestionAnswer
            {
                FileValues = Enumerable.Range(1, count)
                    .Select(i => new FileAnswer { StoredFileName = $"s{i}", OriginalFileName = $"evidence-{i}.pdf", PageCount = 1, FileSizeBytes = 10 })
                    .ToList()
            }
        };

    [Fact]
    public void MaxEvidenceFiles_IsTheLimitTheRuleEnforces()
    {
        Assert.Equal(EvidenceUploadLimits.MaxFiles, WithFiles(0).MaxEvidenceFiles);
    }

    [Fact]
    public void AtLimit_IsNotSetByTheFileCount()
    {
        // 100 pages: the page limit is on but not reached by six one-page files.
        var model = WithFiles(EvidenceUploadLimits.MaxFiles, maxEvidencePages: 100);

        Assert.Equal(6, model.UploadedFiles.Count);
        Assert.False(model.AtLimit);
    }
}
