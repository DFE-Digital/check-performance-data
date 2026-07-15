using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

public sealed class RequestLifecycleEventsTests
{
    [Fact]
    public void CorrectDataConfirmedEvent_projects_fields_with_hidden_reference()
    {
        var e = new CorrectDataConfirmedEvent
        {
            ReferenceNumber = "CYPMD_KS4June_ABC1234",
            CheckingWindowType = "KS4June",
        };

        Assert.Equal("correct_data_confirmed", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("CYPMD_KS4June_ABC1234", byName["reference_number"].Value);
        Assert.True(byName["reference_number"].Hidden);
        Assert.Equal("KS4June", byName["checking_window_type"].Value);
    }

    [Fact]
    public void AmendmentRequestDeletedEvent_projects_fields_with_hidden_reference()
    {
        var e = new AmendmentRequestDeletedEvent
        {
            ReferenceNumber = "CYPMD_KS4June_ABC1234",
            WasHardDeleted = true,
        };

        Assert.Equal("amendment_request_deleted", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("CYPMD_KS4June_ABC1234", byName["reference_number"].Value);
        Assert.True(byName["reference_number"].Hidden);
        Assert.True((bool)byName["was_hard_deleted"].Value!);
    }

    [Fact]
    public void ConfirmationDeletedEvent_projects_hidden_reference()
    {
        var e = new ConfirmationDeletedEvent { ReferenceNumber = "CYPMD_KS4June_ABC1234" };

        Assert.Equal("confirmation_deleted", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("CYPMD_KS4June_ABC1234", byName["reference_number"].Value);
        Assert.True(byName["reference_number"].Hidden);
    }
}
