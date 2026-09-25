using DfE.CheckPerformanceData.Application.Audit;

namespace DfE.CheckPerformanceData.Application.UnitTests.Audit;

// AB#294592: the audit log reads an egress row's NewValues back. The writer serialises with
// JsonSerializerDefaults.Web (camelCase), and rows written before this ticket carry fewer fields,
// so the reader must be tolerant and must never throw on a payload it does not understand.
public sealed class EgressAuditPayloadTests
{
    [Fact]
    public void Parses_the_success_payload_the_transfer_writes()
    {
        const string json = """
            {"outcome":"Succeeded","windowId":"f34d285b-8660-4d12-9c30-787328deaa0a","outputTypes":["NewLearners","RemoveLearners"],
             "files":[{"outputType":"RemoveLearners","fileName":"CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv","records":2,"sha256":"ABC"}],
             "targetContainer":"cypmd/extracts_input","transferredBy":"Ops One","transferredAtUtc":"2026-06-08T14:38:00Z"}
            """;

        var payload = EgressAuditPayload.TryParse(json);

        Assert.NotNull(payload);
        Assert.Equal(Guid.Parse("f34d285b-8660-4d12-9c30-787328deaa0a"), payload!.WindowId);
        Assert.Equal(new[] { "NewLearners", "RemoveLearners" }, payload.OutputTypes);
        Assert.Equal("Ops One", payload.TransferredBy);
        Assert.Null(payload.StartedByName);
    }

    [Fact]
    public void Parses_the_failure_payload_with_and_without_the_new_fields()
    {
        var legacy = EgressAuditPayload.TryParse("""{"outcome":"Failed","reason":"Blob upload refused"}""");
        Assert.NotNull(legacy);
        Assert.Null(legacy!.WindowId);
        Assert.Null(legacy.OutputTypes);
        Assert.Null(legacy.TransferredBy);

        var current = EgressAuditPayload.TryParse("""{"outcome":"Failed","windowId":"f34d285b-8660-4d12-9c30-787328deaa0a","outputTypes":["RemoveLearners"],"transferredBy":"Ops Two","reason":"Blob upload refused"}""");
        Assert.Equal("Ops Two", current!.TransferredBy);
        Assert.Equal(new[] { "RemoveLearners" }, current.OutputTypes);
    }

    [Fact]
    public void Parses_the_generic_captures_pascal_case_insert_row_for_a_run()
    {
        // PortalDbContext's capture records the run row's creation (the pull) with PascalCase keys;
        // the log still needs its window and the person who started the run, and must not invent
        // output types or a transfer.
        var pulled = EgressAuditPayload.TryParse("""{"Id":"33333333-3333-3333-3333-333333333333","WindowId":"f34d285b-8660-4d12-9c30-787328deaa0a","Status":"Pulled","StartedByName":"Ops One","StartedByEmail":"ops.one@education.gov.uk","StartedAtUtc":"2026-06-08T14:00:00Z"}""");
        Assert.NotNull(pulled);
        Assert.Equal(Guid.Parse("f34d285b-8660-4d12-9c30-787328deaa0a"), pulled!.WindowId);
        Assert.Equal("Ops One", pulled.StartedByName);
        Assert.Null(pulled.OutputTypes);
        Assert.Null(pulled.TransferredBy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void A_missing_or_unreadable_payload_is_null_never_a_throw(string? json)
        => Assert.Null(EgressAuditPayload.TryParse(json));
}
