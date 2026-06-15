using System.Text.Json;
using DfE.CheckPerformanceData.Application.RequestSubmission;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// Pins the consumer parse seam: the worker deserialises a queue body into the
/// same <see cref="RequestDocument"/> contract the producer serialises. A body
/// that is malformed or missing a required field yields <c>null</c> (the worker
/// treats that as an unparseable message) rather than a half-built document.
/// </summary>
public sealed class RequestDocumentParserTests
{
    [Fact]
    public void Parse_RoundTripsDocument_SerializedWithDefaultOptions()
    {
        // RequestService enqueues with bare JsonSerializer.Serialize(document) —
        // pin that the worker-side parser reads that exact wire format back.
        var doc = new RequestDocument
        {
            ChangeRequestId = Guid.Parse("99999999-8888-7777-6666-555555555555"),
            ReferenceNumber = "REF-RT",
            SubmittedAt = new DateTime(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc),
            SubmittedBy = new UserDetails { UserId = "u1", DisplayName = "Alice" },
            CheckingWindowId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            CheckingWindowType = "KS4June",
            RequestTypeCode = "Remove - pupil-died",
            School = new SchoolDetails { Urn = "123456", Name = "Test School" },
            Pupil = new PupilDetails
            {
                Id = "p1", CypmdId = "c1", Firstname = "Bob", Surname = "Smith",
                DateOfBirth = "01/01/2010", Sex = "M", Age = 15, Upn = "UPN1", Pincl = 402
            },
            Answers =
            [
                new AnswerRecord
                {
                    QuestionId = "date-removed-from-roll", QuestionTitle = "When?",
                    Type = "Date", Value = "15/02/2025", RawValue = "2025-02-15"
                }
            ]
        };

        var parsed = RequestDocumentParser.Parse(JsonSerializer.Serialize(doc));

        Assert.NotNull(parsed);
        Assert.Equal("Remove - pupil-died", parsed!.RequestTypeCode);
        Assert.Equal("REF-RT", parsed.ReferenceNumber);
        Assert.Equal(402, parsed.Pupil.Pincl);
        Assert.Single(parsed.Answers);
        Assert.Equal("2025-02-15", parsed.Answers[0].RawValue);
        Assert.Equal("15/02/2025", parsed.Answers[0].Value);
    }

    [Fact]
    public void Parse_ValidMessage_ReturnsDocument()
    {
        const string json = """
            {
              "ChangeRequestId": "11111111-2222-3333-4444-555555555555",
              "CheckingWindowId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "CheckingWindowType": "KS4June",
              "RequestTypeCode": "Deceased",
              "ReferenceNumber": "REF-001",
              "SubmittedAt": "2026-05-14T10:00:00Z",
              "School": { "Urn": "123456", "Name": "Test School" },
              "SubmittedBy": { "UserId": "u1", "DisplayName": "Alice" },
              "Pupil": { "Id": "p1", "CypmdId": "c1", "Firstname": "Bob", "Surname": "Smith", "DateOfBirth": "01/01/2010", "Sex": "M", "Age": 15, "Upn": "UPN1" },
              "Answers": [
                { "QuestionId": "year-group-change", "QuestionTitle": "year group", "Type": "text", "Value": "Lower" }
              ]
            }
            """;

        var parsed = RequestDocumentParser.Parse(json);

        Assert.NotNull(parsed);
        Assert.Equal("Deceased", parsed!.RequestTypeCode);
        Assert.Equal("KS4June", parsed.CheckingWindowType);
        Assert.Equal("REF-001", parsed.ReferenceNumber);
        Assert.Single(parsed.Answers);
        Assert.Equal("Lower", parsed.Answers[0].Value);
    }

    [Fact]
    public void Parse_CaseInsensitiveProperties_Succeeds()
    {
        // The producer serialises PascalCase; this confirms case-insensitive parsing.
        const string json = """
            {
              "changerequestid": "11111111-2222-3333-4444-555555555555",
              "checkingwindowtype": "KS2",
              "requesttypecode": "Elective home education",
              "referencenumber": "REF-2",
              "school": { "urn": "9", "name": "S" },
              "submittedby": { "userid": "u", "displayname": "x" },
              "pupil": { "id": "p", "cypmdid": "c", "firstname": "A", "surname": "B", "dateofbirth": "01/01/2010", "sex": "F", "age": 8, "upn": "U" },
              "answers": []
            }
            """;

        var parsed = RequestDocumentParser.Parse(json);

        Assert.NotNull(parsed);
        Assert.Equal("Elective home education", parsed!.RequestTypeCode);
    }

    [Fact]
    public void Parse_ChangeRequestId_RoundTrips()
    {
        // The worker updates ChangeRequests by this Id, so it must survive the wire.
        const string json = """
            {
              "ChangeRequestId": "99999999-8888-7777-6666-555555555555",
              "CheckingWindowId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "CheckingWindowType": "KS4June",
              "RequestTypeCode": "Deceased",
              "ReferenceNumber": "REF-001",
              "School": { "Urn": "123456", "Name": "Test School" },
              "SubmittedBy": { "UserId": "u1", "DisplayName": "Alice" },
              "Pupil": { "Id": "p1", "CypmdId": "c1", "Firstname": "Bob", "Surname": "Smith", "DateOfBirth": "01/01/2010", "Sex": "M", "Age": 15, "Upn": "UPN1" },
              "Answers": []
            }
            """;

        var parsed = RequestDocumentParser.Parse(json);

        Assert.NotNull(parsed);
        Assert.Equal(Guid.Parse("99999999-8888-7777-6666-555555555555"), parsed!.ChangeRequestId);
    }

    [Fact]
    public void Parse_MissingChangeRequestId_ReturnsNull()
    {
        // Pre-release contract: ChangeRequestId is required; a document without it
        // is unparseable and goes through poison-message handling.
        const string json = """
            {
              "CheckingWindowId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "CheckingWindowType": "KS4June",
              "RequestTypeCode": "Deceased",
              "ReferenceNumber": "REF-001",
              "School": { "Urn": "123456", "Name": "Test School" },
              "SubmittedBy": { "UserId": "u1", "DisplayName": "Alice" },
              "Pupil": { "Id": "p1", "CypmdId": "c1", "Firstname": "Bob", "Surname": "Smith", "DateOfBirth": "01/01/2010", "Sex": "M", "Age": 15, "Upn": "UPN1" },
              "Answers": []
            }
            """;

        Assert.Null(RequestDocumentParser.Parse(json));
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsNull()
    {
        Assert.Null(RequestDocumentParser.Parse("{ not valid"));
    }

    [Fact]
    public void Parse_MissingRequiredField_ReturnsNull()
    {
        // Pupil is required; omitting it must not yield a half-built document.
        const string json = """
            { "CheckingWindowType": "KS4June", "RequestTypeCode": "Deceased", "ReferenceNumber": "R", "Answers": [] }
            """;

        Assert.Null(RequestDocumentParser.Parse(json));
    }

    [Fact]
    public void Parse_NullInput_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RequestDocumentParser.Parse(null!));
    }
}
