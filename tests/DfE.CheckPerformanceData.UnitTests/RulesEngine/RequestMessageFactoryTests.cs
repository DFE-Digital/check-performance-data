using System.Text.Json;
using DfE.CheckPerformanceData.Domain.QueueMessages;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// Pins the normalised parse behaviour: every well-formed message body becomes
/// a single <see cref="PendingRequestMessage"/>, regardless of any on-the-wire
/// <c>DecisionType</c> the producer set. The rules engine — not the producer —
/// decides what happens next.
/// </summary>
public sealed class RequestMessageFactoryTests
{
    [Fact]
    public void Parse_WithScrutinyDecisionType_ReturnsPendingRequestMessage()
    {
        const string json = """
            {
              "RequestId": "11111111-2222-3333-4444-555555555555",
              "DecisionType": "Scrutiny",
              "CheckingWindowId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "CheckingWindowType": "KS4",
              "WhatToChange": "Deceased",
              "Reason": "Pupil deceased.",
              "School": { "Urn": "123456", "Name": "Test School" },
              "SubmittedBy": { "UserId": "u1", "DisplayName": "Alice" },
              "Pupil": { "Id": "p1", "Firstname": "Bob", "Surname": "Smith", "Sex": "M", "Age": 15 },
              "Answers": [
                { "QuestionId": "year-group-change", "QuestionTitle": "year group", "Type": "text", "Value": "Lower" }
              ],
              "Uploads": [],
              "ReferenceNumber": "REF-001",
              "Status": "submitted",
              "SubmittedAt": "2026-05-14T10:00:00Z"
            }
            """;

        var parsed = RequestMessageFactory.Parse(json);

        Assert.NotNull(parsed);
        var pending = Assert.IsType<PendingRequestMessage>(parsed);
        Assert.Equal("Deceased", pending.WhatToChange);
        Assert.Equal("KS4", pending.CheckingWindowType);
        Assert.Equal("Pupil deceased.", pending.Reason);
        Assert.Single(pending.Answers);
        Assert.Equal("Lower", pending.Answers[0].Value);
    }

    [Fact]
    public void Parse_WithoutDecisionType_StillSucceeds()
    {
        // The factory deliberately ignores DecisionType — its absence is harmless.
        const string json = """
            {
              "CheckingWindowType": "KS2",
              "WhatToChange": "Elective home education",
              "Reason": "moved to home schooling",
              "School": { "Urn": "999", "Name": "S" },
              "SubmittedBy": { "UserId": "u", "DisplayName": "x" },
              "Pupil": { "Id": "p", "Firstname": "A", "Surname": "B", "Sex": "F", "Age": 8 },
              "Answers": [],
              "Uploads": []
            }
            """;

        var parsed = RequestMessageFactory.Parse(json);

        Assert.NotNull(parsed);
        Assert.IsType<PendingRequestMessage>(parsed);
        Assert.Equal("Elective home education", parsed.WhatToChange);
    }

    [Fact]
    public void Parse_WithUnknownDecisionTypeOnWire_StillSucceeds()
    {
        // The wire value is informational only now — a producer mistake here
        // must not force an auto-decision. The rules engine decides.
        const string json = """
            {
              "DecisionType": "TotallyMadeUp",
              "CheckingWindowType": "Post16",
              "WhatToChange": "Other",
              "School": { "Urn": "0", "Name": "x" },
              "SubmittedBy": { "UserId": "u", "DisplayName": "x" },
              "Pupil": { "Id": "p", "Firstname": "A", "Surname": "B", "Sex": "F", "Age": 17 },
              "Answers": [],
              "Uploads": []
            }
            """;

        var parsed = RequestMessageFactory.Parse(json);

        Assert.NotNull(parsed);
        Assert.IsType<PendingRequestMessage>(parsed);
    }

    [Fact]
    public void Parse_CarriesUploads_OnPendingMessage()
    {
        const string json = """
            {
              "CheckingWindowType": "KS4",
              "WhatToChange": "Terminal/Critical illness",
              "Reason": "evidence enclosed",
              "School": { "Urn": "1", "Name": "x" },
              "SubmittedBy": { "UserId": "u", "DisplayName": "x" },
              "Pupil": { "Id": "p", "Firstname": "A", "Surname": "B", "Sex": "F", "Age": 16 },
              "Answers": [],
              "Uploads": [
                { "filename": "letter.pdf", "id": "33333333-3333-3333-3333-333333333333", "comments": "GP letter" }
              ]
            }
            """;

        var parsed = (PendingRequestMessage)RequestMessageFactory.Parse(json)!;

        Assert.Single(parsed.Uploads);
        Assert.Equal("letter.pdf", parsed.Uploads[0].Filename);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), parsed.Uploads[0].Id);
    }

    [Fact]
    public void Parse_OnMalformedJson_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => RequestMessageFactory.Parse("{ not valid"));
    }

    [Fact]
    public void Parse_OnNullInput_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RequestMessageFactory.Parse(null!));
    }

    [Fact]
    public void PendingRequestMessage_ImplementsIRequestMessageUploads()
    {
        // The handler in Step 5 will rely on this so the existing attachment-upload path keeps working.
        var msg = new PendingRequestMessage();
        Assert.IsAssignableFrom<IRequestMessageUploads>(msg);
    }
}
