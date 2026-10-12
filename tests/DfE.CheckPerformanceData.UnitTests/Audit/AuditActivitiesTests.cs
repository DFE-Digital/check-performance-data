using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Audit;

// AB#294592: the one mapping from an audit row's EntityType/Action to what the audit log shows.
// Egress rows are the only ones with an outcome; every other row is an activity with no status.
public sealed class AuditActivitiesTests
{
    [Theory]
    [InlineData("EgressRun", "Data egress")]
    [InlineData("CheckingWindow", "Checking window")]
    [InlineData("ChangeRequest", "Amendment request")]
    [InlineData("ContentBundle", "Content import")]
    [InlineData("DlqMessage", "Dead letter queue")]
    [InlineData("Setting", "System setting")]
    [InlineData("WindowAdmin", "Window admin")]
    public void Known_entity_types_have_a_plain_english_label(string entityType, string expected)
        => Assert.Equal(expected, AuditActivities.Label(entityType));

    [Theory]
    [InlineData("PageNodeVersionThing", "Page node version thing")]
    [InlineData("Widget", "Widget")]
    [InlineData("", "")]
    public void Unknown_entity_types_are_humanised_rather_than_shown_raw(string entityType, string expected)
        => Assert.Equal(expected, AuditActivities.Label(entityType));

    [Fact]
    public void Only_egress_transfer_rows_have_an_outcome()
    {
        Assert.Equal(AuditOutcome.Success, AuditActivities.OutcomeOf("EgressRun", "Transfer"));
        Assert.Equal(AuditOutcome.Failed, AuditActivities.OutcomeOf("EgressRun", "TransferFailed"));
        Assert.Null(AuditActivities.OutcomeOf("EgressRun", "Insert"));
        Assert.Null(AuditActivities.OutcomeOf("CheckingWindow", "Transfer"));
    }

    [Fact]
    public void An_early_closure_is_a_success()
    {
        // AB#301022: the row is only ever written once the exercise has closed, so it has one outcome.
        Assert.Equal(AuditOutcome.Success, AuditActivities.OutcomeOf("WindowAdmin", "ClosedEarly"));
        Assert.Null(AuditActivities.OutcomeOf("WindowAdmin", "Update"));
        Assert.Null(AuditActivities.OutcomeOf("CheckingExercise", "ClosedEarly"));
    }

    [Fact]
    public void An_automatic_hand_over_is_a_success()
    {
        // AB#302158: the row is only written for a run that sent or cancelled something, so it
        // has one outcome. A run that failed writes no row; it is retried on the next tick.
        Assert.Equal("RequestsSentAutomatically", AuditActivities.RequestsSentAutomaticallyAction);
        Assert.Equal(AuditOutcome.Success, AuditActivities.OutcomeOf("WindowAdmin", "RequestsSentAutomatically"));
        Assert.Null(AuditActivities.OutcomeOf("CheckingExercise", "RequestsSentAutomatically"));
    }

    [Theory]
    [InlineData("Success", AuditOutcome.Success)]
    [InlineData("failed", AuditOutcome.Failed)]
    [InlineData("SUCCESS", AuditOutcome.Success)]
    public void Outcome_names_parse_case_insensitively(string value, AuditOutcome expected)
        => Assert.Equal(expected, AuditActivities.TryParseOutcome(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("Draft")]
    public void Anything_but_an_outcome_name_is_no_filter(string? value)
        => Assert.Null(AuditActivities.TryParseOutcome(value));

    [Fact]
    public void The_filter_offers_success_then_failed_and_labels_them()
    {
        Assert.Equal(new[] { AuditOutcome.Success, AuditOutcome.Failed }, AuditActivities.AllOutcomes);
        Assert.Equal("Success", AuditActivities.OutcomeLabel(AuditOutcome.Success));
        Assert.Equal("Failed", AuditActivities.OutcomeLabel(AuditOutcome.Failed));
    }
}
