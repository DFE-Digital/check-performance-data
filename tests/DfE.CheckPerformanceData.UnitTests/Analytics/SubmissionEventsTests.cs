using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

public sealed class SubmissionEventsTests
{
    [Fact]
    public void RequestSubmittedEvent_projects_fields_with_hidden_reference()
    {
        var e = new RequestSubmittedEvent
        {
            WhatToChange = "Remove",
            CheckingWindowType = "KS4June",
            ReferenceNumber = "CYPMD_KS4June_ABC1234",
        };

        Assert.Equal("request_submitted", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("Remove", byName["what_to_change"].Value);
        Assert.Equal("KS4June", byName["checking_window_type"].Value);
        Assert.Equal("CYPMD_KS4June_ABC1234", byName["reference_number"].Value);
        // reference_number is masked until the DPIA classification is made.
        Assert.True(byName["reference_number"].Hidden);
    }

    [Fact]
    public void RequestSubmissionFailedEvent_projects_failure_reason()
    {
        var e = new RequestSubmissionFailedEvent
        {
            FailureReason = "duplicate_request",
            WhatToChange = "Remove",
            CheckingWindowType = "KS4June",
        };

        Assert.Equal("request_submission_failed", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("duplicate_request", byName["failure_reason"].Value);
        Assert.Equal("Remove", byName["what_to_change"].Value);
        Assert.Equal("KS4June", byName["checking_window_type"].Value);
    }

    [Fact]
    public void DraftSavedEvent_projects_status_with_hidden_reference()
    {
        var e = new DraftSavedEvent
        {
            Status = "ReadyToSubmit",
            WhatToChange = "Remove",
            CheckingWindowType = "KS4June",
            ReferenceNumber = "CYPMD_KS4June_ABC1234",
        };

        Assert.Equal("draft_saved", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("ReadyToSubmit", byName["status"].Value);
        Assert.Equal("Remove", byName["what_to_change"].Value);
        Assert.True(byName["reference_number"].Hidden);
    }
}
