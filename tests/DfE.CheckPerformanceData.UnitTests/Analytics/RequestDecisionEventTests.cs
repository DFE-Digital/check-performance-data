using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

public sealed class RequestDecisionEventTests
{
    private static RequestDecisionEvent NewEvent() => new()
    {
        DecisionStatus = "AutoApproved",
        OutcomeKey = "Inclusion",
        MatchedRuleId = "EAL-ACC-ADDBACK-1",
        RulesVersion = "2026.06.11-01",
        RequestTypeCode = "Include",
        CheckingWindowType = "KS4June",
    };

    [Fact]
    public void EventType_is_request_decision()
    {
        Assert.Equal("request_decision", NewEvent().EventType);
    }

    [Fact]
    public void Fields_project_the_decision_metadata()
    {
        var fields = NewEvent().Fields.ToDictionary(f => f.Name, f => f.Value);

        Assert.Equal("AutoApproved", fields["decision_status"]);
        Assert.Equal("Inclusion", fields["outcome_key"]);
        Assert.Equal("EAL-ACC-ADDBACK-1", fields["matched_rule_id"]);
        Assert.Equal("2026.06.11-01", fields["rules_version"]);
        Assert.Equal("Include", fields["request_type_code"]);
        Assert.Equal("KS4June", fields["checking_window_type"]);
        Assert.Equal(false, fields["is_synthetic_fallback"]);
    }

    [Fact]
    public void Fields_carry_no_hidden_data()
    {
        // The decision event is metadata-only; nothing in it is PII, so nothing
        // should be routed to the masked channel.
        Assert.DoesNotContain(NewEvent().Fields, f => f.Hidden);
    }
}
