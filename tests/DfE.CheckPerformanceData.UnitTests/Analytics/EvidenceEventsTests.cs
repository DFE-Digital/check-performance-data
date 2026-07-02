using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

public sealed class EvidenceEventsTests
{
    [Fact]
    public void EvidenceUploadAttemptedEvent_success_projects_metrics_without_failure_reason()
    {
        var e = new EvidenceUploadAttemptedEvent { Outcome = "success", PageCount = 3, FileSizeBytes = 2048 };

        Assert.Equal("evidence_upload_attempted", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("success", byName["outcome"].Value);
        Assert.Equal(3, byName["page_count"].Value);
        Assert.Equal(2048L, byName["file_size_bytes"].Value);
        Assert.Null(byName["failure_reason"].Value);   // null on success → omitted at send
    }

    [Fact]
    public void EvidenceUploadAttemptedEvent_failure_projects_failure_reason()
    {
        var e = new EvidenceUploadAttemptedEvent { Outcome = "failed", FailureReason = "too_large", FileSizeBytes = 99999 };

        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("failed", byName["outcome"].Value);
        Assert.Equal("too_large", byName["failure_reason"].Value);
    }

    [Fact]
    public void EvidenceFileRemovedEvent_projects_before_and_after_counts()
    {
        var e = new EvidenceFileRemovedEvent { FilesBefore = 2, FilesAfter = 1 };

        Assert.Equal("evidence_file_removed", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal(2, byName["files_before"].Value);
        Assert.Equal(1, byName["files_after"].Value);
    }

    [Fact]
    public void EvidenceContinueEvent_projects_counts_and_text_length()
    {
        var e = new EvidenceContinueEvent { FileCount = 2, PageCount = 5, EvidenceTextLength = 140 };

        Assert.Equal("evidence_continue", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal(2, byName["file_count"].Value);
        Assert.Equal(5, byName["page_count"].Value);
        Assert.Equal(140, byName["evidence_text_length"].Value);
    }
}
