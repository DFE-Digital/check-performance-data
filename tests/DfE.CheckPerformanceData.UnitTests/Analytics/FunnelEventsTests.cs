using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

public sealed class FunnelEventsTests
{
    [Fact]
    public void ChangeTypeSelectedEvent_projects_what_to_change()
    {
        var e = new ChangeTypeSelectedEvent { WhatToChange = "Remove", CheckingWindowType = "KS4June" };

        Assert.Equal("change_type_selected", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("Remove", byName["what_to_change"].Value);
        Assert.Equal("KS4June", byName["checking_window_type"].Value);
    }

    [Fact]
    public void DraftResumedEvent_projects_fields_with_hidden_reference()
    {
        var e = new DraftResumedEvent
        {
            ReferenceNumber = "CYPMD_KS4June_ABC1234",
            WhatToChange = "Remove",
            CheckingWindowType = "KS4June",
        };

        Assert.Equal("draft_resumed", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("CYPMD_KS4June_ABC1234", byName["reference_number"].Value);
        Assert.True(byName["reference_number"].Hidden);
        Assert.Equal("Remove", byName["what_to_change"].Value);
    }
}
