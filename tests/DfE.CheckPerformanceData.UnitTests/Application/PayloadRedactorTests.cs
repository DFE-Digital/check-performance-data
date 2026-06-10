using DfE.CheckPerformanceData.Application.Queue;

namespace DfE.CheckPerformanceData.Application.UnitTests.Queue;

public sealed class PayloadRedactorTests
{
    private const string FullPayload = """
        {
          "ReferenceNumber": "REF-12345",
          "SubmittedAt": "2026-06-10T09:30:00Z",
          "SubmittedBy": { "UserId": "user-1", "DisplayName": "Jane Teacher" },
          "CheckingWindowId": "11111111-1111-1111-1111-111111111111",
          "CheckingWindowType": "KS4June",
          "WhatToChange": "AddPupil",
          "School": { "Urn": "100123", "Name": "Greendale Secondary" },
          "Pupil": {
            "Id": "pid-001",
            "CypmdId": "cyp-777",
            "Firstname": "Ann",
            "Surname": "Pupilson",
            "DateOfBirth": "2010-04-01",
            "Sex": "F",
            "Age": 14,
            "Upn": "X999888777666"
          },
          "MatchedPupil": {
            "Id": "pid-002",
            "CypmdId": "cyp-888",
            "Firstname": "Annette",
            "Surname": "Robinson",
            "DateOfBirth": "2010-05-02",
            "Sex": "F",
            "Age": 14,
            "Upn": "Y111222333444"
          },
          "Answers": [
            {
              "QuestionId": "q1",
              "QuestionTitle": "New surname",
              "Type": "text",
              "Value": "SensitiveSurnameValue"
            }
          ]
        }
        """;

    private static string Redact(string payload) => new PayloadRedactor().Redact(payload);

    [Theory]
    [InlineData("X999888777666")]   // Pupil.Upn
    [InlineData("Ann")]             // Pupil.Firstname
    [InlineData("Pupilson")]        // Pupil.Surname
    [InlineData("2010-04-01")]      // Pupil.DateOfBirth
    [InlineData("pid-001")]         // Pupil.Id
    [InlineData("cyp-777")]         // Pupil.CypmdId
    public void Redact_Masks_PupilIdentifiers(string sensitiveValue)
    {
        var result = Redact(FullPayload);

        Assert.DoesNotContain(sensitiveValue, result);
    }

    [Theory]
    [InlineData("Y111222333444")]   // MatchedPupil.Upn
    [InlineData("Annette")]         // MatchedPupil.Firstname
    [InlineData("Robinson")]        // MatchedPupil.Surname
    [InlineData("2010-05-02")]      // MatchedPupil.DateOfBirth
    [InlineData("pid-002")]         // MatchedPupil.Id
    [InlineData("cyp-888")]         // MatchedPupil.CypmdId
    public void Redact_Masks_MatchedPupilIdentifiers(string sensitiveValue)
    {
        var result = Redact(FullPayload);

        Assert.DoesNotContain(sensitiveValue, result);
    }

    [Fact]
    public void Redact_Masks_SubmittedByDisplayName()
    {
        var result = Redact(FullPayload);

        Assert.DoesNotContain("Jane Teacher", result);
    }

    [Fact]
    public void Redact_Masks_AnswerValue()
    {
        var result = Redact(FullPayload);

        Assert.DoesNotContain("SensitiveSurnameValue", result);
    }

    [Theory]
    [InlineData("REF-12345")]               // ReferenceNumber
    [InlineData("11111111-1111-1111")]      // CheckingWindowId
    [InlineData("KS4June")]                 // CheckingWindowType
    [InlineData("AddPupil")]                // WhatToChange
    [InlineData("100123")]                  // School.Urn
    [InlineData("Greendale Secondary")]     // School.Name
    [InlineData("2026-06-10T09:30:00")]     // SubmittedAt
    public void Redact_Preserves_SafeMetadata(string safeValue)
    {
        var result = Redact(FullPayload);

        Assert.Contains(safeValue, result);
    }

    [Fact]
    public void Redact_Preserves_Structure_KeysRemainVisible()
    {
        var result = Redact(FullPayload);

        // Operators still need to see the shape of the message.
        Assert.Contains("Pupil", result);
        Assert.Contains("Upn", result);
        Assert.Contains("Answers", result);
        Assert.Contains("QuestionTitle", result);
    }

    [Fact]
    public void Redact_Preserves_NonSensitiveAnswerMetadata()
    {
        var result = Redact(FullPayload);

        // The question itself is safe to show; only the answer Value is masked.
        Assert.Contains("New surname", result);
    }

    [Fact]
    public void Redact_ToleratesMalformedJson_WithoutLeakingInput()
    {
        var result = Redact("not valid json with Upn X999");

        // A non-parseable payload must not surface raw content verbatim.
        Assert.DoesNotContain("X999", result);
    }
}
