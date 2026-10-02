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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    public void An_unreadable_payload_is_null(string? json)
        => Assert.Null(WindowAdminAuditPayload.TryParse(json));
}
