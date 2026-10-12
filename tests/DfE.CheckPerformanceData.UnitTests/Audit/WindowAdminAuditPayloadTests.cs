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

    [Fact]
    public void An_extension_payload_gives_the_id_the_actor_the_dates_and_the_school_changes()
    {
        // AB#307773: what the extension writers record.
        const string json = """
            {"windowId":"11111111-1111-1111-1111-111111111111","exerciseType":"PupilData",
             "extensionId":"22222222-2222-2222-2222-222222222222","actorName":"Ops One",
             "endDate":"2026-11-12T17:00:00","previousEndDate":"2026-11-05T17:00:00",
             "newEndDate":"2026-11-12T17:00:00","schoolsAdded":["9334290"],"schoolsRemoved":[],
             "schoolCount":4}
            """;

        var payload = WindowAdminAuditPayload.TryParse(json);

        Assert.NotNull(payload);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), payload!.ExtensionId);
        Assert.Equal("Ops One", payload.ActorName);
        Assert.Equal(new DateTime(2026, 11, 12, 17, 0, 0), payload.EndDate);
        Assert.Equal(new DateTime(2026, 11, 5, 17, 0, 0), payload.PreviousEndDate);
        Assert.Equal(new DateTime(2026, 11, 12, 17, 0, 0), payload.NewEndDate);
        Assert.Equal(["9334290"], payload.SchoolsAdded);
        Assert.Empty(payload.SchoolsRemoved!);
        Assert.Equal(4, payload.SchoolCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    public void An_unreadable_payload_is_null(string? json)
        => Assert.Null(WindowAdminAuditPayload.TryParse(json));
}
