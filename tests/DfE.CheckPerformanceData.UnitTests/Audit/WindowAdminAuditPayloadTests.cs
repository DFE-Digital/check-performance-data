using DfE.CheckPerformanceData.Application.Audit;

namespace DfE.CheckPerformanceData.Application.UnitTests.Audit;

// AB#301022: the fields the audit log reads back out of a WindowAdmin / ClosedEarly row. The
// repository writes camelCase JSON; an unreadable payload is null and the row still lists.
public sealed class WindowAdminAuditPayloadTests
{
    [Fact]
    public void The_payload_the_repository_writes_is_read_back()
    {
        const string json = """
            {"windowId":"11111111-1111-1111-1111-111111111111","windowTitle":"Key Stage 4 June",
             "exerciseType":"ResultsEnquiry","scheduledEnd":"2027-12-15T17:00:00",
             "newEndDate":"2026-09-03T10:38:59","closedEarly":true,"closedBy":"Banks Jamgbadi",
             "closedAtUtc":"2026-09-03T09:39:00Z"}
            """;

        var payload = WindowAdminAuditPayload.TryParse(json);

        Assert.NotNull(payload);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), payload!.WindowId);
        Assert.Equal("ResultsEnquiry", payload.ExerciseType);
        Assert.Equal(new DateTime(2027, 12, 15, 17, 0, 0), payload.ScheduledEnd);
        Assert.Equal("Banks Jamgbadi", payload.ClosedBy);
    }

    [Fact]
    public void An_automatic_hand_over_payload_gives_the_exercise_and_the_counts()
    {
        // AB#302158: what WindowAdminAuditWriter writes. It has no closedBy and no scheduledEnd.
        const string json = """
            {"windowId":"11111111-1111-1111-1111-111111111111","windowTitle":"Key Stage 4 June",
             "exerciseType":"PupilData","exerciseEnd":"2026-11-02T17:00:00","requestsSent":3,
             "draftsCancelled":2,"automatic":true,"ranAtUtc":"2026-11-02T18:00:00Z"}
            """;

        var payload = WindowAdminAuditPayload.TryParse(json);

        Assert.NotNull(payload);
        Assert.Equal("PupilData", payload!.ExerciseType);
        Assert.Equal(3, payload.RequestsSent);
        Assert.Equal(2, payload.DraftsCancelled);
        Assert.Null(payload.ClosedBy);
        Assert.Null(payload.ScheduledEnd);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    public void An_unreadable_payload_is_null(string? json)
        => Assert.Null(WindowAdminAuditPayload.TryParse(json));
}
