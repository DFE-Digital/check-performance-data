using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

public sealed class RuleContextMapperTests
{
    private readonly IRuleContextMapper _sut = new RuleContextMapper();

    // --- OutcomeKey ---

    [Theory]
    [InlineData("Merge",                                    "MergePupils")]
    [InlineData("Include",                                  "Inclusion")]
    [InlineData("Remove - pupil-died",                      "Deceased")]
    [InlineData("Remove - elective-home-education",         "ElectiveHomeEducation")]
    [InlineData("  Remove - elective-home-education  ",     "ElectiveHomeEducation")] // whitespace-tolerant
    [InlineData("remove - ELECTIVE-HOME-EDUCATION",         "ElectiveHomeEducation")] // case-insensitive
    [InlineData("Remove - permanently-left-england",        "PermanentlyLeftEngland")]
    [InlineData("Remove - social-care-involvement",         "SocialCareInvolvement")]
    [InlineData("Remove - child-missing-education",         "PupilMissingInEducation")]
    [InlineData("Remove - life-limiting-illness",           "TerminalCriticalIllness")]
    [InlineData("Remove - permanent-exclusion",             "AdmittedFollowingPermanentExclusion")]
    [InlineData("Remove - permanently-excluded",            "PermanentlyExcludedFromCurrentSchool")]
    public void Maps_KnownWhatToChange_ToOutcomeKey(string whatToChange, string expected)
    {
        var msg = NewMessage(whatToChange);

        var ctx = _sut.Map(msg);

        Assert.Equal(expected, ctx.OutcomeKey);
    }

    [Fact]
    public void Maps_UnknownWhatToChange_ToUnknownSentinel()
    {
        var msg = NewMessage("nonsense reason");

        var ctx = _sut.Map(msg);

        Assert.Equal(AnswerFieldMap.UnknownOutcomeKey, ctx.OutcomeKey);
    }

    [Fact]
    public void Maps_NullWhatToChange_ToUnknownSentinel()
    {
        var msg = NewMessage(whatToChange: null!);

        var ctx = _sut.Map(msg);

        Assert.Equal(AnswerFieldMap.UnknownOutcomeKey, ctx.OutcomeKey);
    }

    // --- KeyStage normalisation ---

    [Theory]
    [InlineData("KS2",       "KS2")]
    [InlineData("ks4",       "KS4")]   // case-insensitive
    [InlineData("Post16",    "Post16")]
    [InlineData("16 to 18",  "Post16")] // docx phrasing
    [InlineData("16-18",     "Post16")]
    [InlineData("",          "")]      // unrecognised → empty
    public void Maps_CheckingWindowType_ToCanonicalKeyStage(string raw, string expected)
    {
        var msg = NewMessage("Remove - pupil-died", checkingWindowType: raw);

        var ctx = _sut.Map(msg);

        Assert.Equal(expected, ctx.KeyStage);
    }

    // --- Field projection ---

    [Fact]
    public void Maps_StringAnswer_ToFieldValueStr()
    {
        var msg = NewMessage("Include", answers: new[]
        {
            Answer("inclusion-status-flag", "402")
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Str("402"), ctx.GetField("inclusionFlag"));
    }

    [Fact]
    public void Maps_BoolAnswer_ToFieldValueBool()
    {
        var msg = NewMessage("Remove - life-limiting-illness", answers: new[]
        {
            Answer("terminal-illness", "true"),
            Answer("critical-illness-12m", "yes"),
            Answer("severe-profound-effect", "1"),
            Answer("under-investigation-12m", "no"),
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Bool(true),  ctx.GetField("hasTerminalIllness"));
        Assert.Equal(new FieldValue.Bool(true),  ctx.GetField("hasCriticalIllness12mPlus"));
        Assert.Equal(new FieldValue.Bool(true),  ctx.GetField("illnessHasSevereProfoundEffect"));
        Assert.Equal(new FieldValue.Bool(false), ctx.GetField("underInvestigation12mPlus"));
    }

    [Fact]
    public void Maps_DateAnswer_ToFieldValueDate()
    {
        var msg = NewMessage("Remove - elective-home-education", answers: new[]
        {
            Answer("date-of-removal-from-roll", "2025-02-15")
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Date(new DateOnly(2025, 2, 15)), ctx.GetField("dateOfRemoval"));
    }

    [Fact]
    public void Maps_IsoDateTimeAnswer_TruncatesToDate()
    {
        var msg = NewMessage("Remove - elective-home-education", answers: new[]
        {
            Answer("date-of-removal-from-roll", "2025-02-15T13:45:00Z")
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Date(new DateOnly(2025, 2, 15)), ctx.GetField("dateOfRemoval"));
    }

    [Fact]
    public void Throws_OnMalformedDate()
    {
        var msg = NewMessage("Remove - elective-home-education", answers: new[]
        {
            Answer("date-of-removal-from-roll", "not-a-date")
        });

        var ex = Assert.Throws<RuleContextMappingException>(() => _sut.Map(msg));
        Assert.Contains("dateOfRemoval", ex.Message);
    }

    [Fact]
    public void Throws_OnMalformedBool()
    {
        var msg = NewMessage("Remove - life-limiting-illness", answers: new[]
        {
            Answer("terminal-illness", "maybe")
        });

        var ex = Assert.Throws<RuleContextMappingException>(() => _sut.Map(msg));
        Assert.Contains("hasTerminalIllness", ex.Message);
    }

    [Fact]
    public void EmptyAnswerValue_BecomesUnknown()
    {
        var msg = NewMessage("Remove - life-limiting-illness", answers: new[]
        {
            Answer("terminal-illness", "")
        });

        var ctx = _sut.Map(msg);

        Assert.IsType<FieldValue.Unknown>(ctx.GetField("hasTerminalIllness"));
    }

    [Fact]
    public void MissingAnswer_BecomesUnknown()
    {
        var msg = NewMessage("Remove - life-limiting-illness", answers: Array.Empty<AnswerRecord>());

        var ctx = _sut.Map(msg);

        Assert.IsType<FieldValue.Unknown>(ctx.GetField("hasTerminalIllness"));
    }

    [Fact]
    public void UnmappedQuestionId_IsSilentlyIgnored()
    {
        var msg = NewMessage("Include", answers: new[]
        {
            Answer("some-extra-question", "value"),
            Answer("inclusion-status-flag", "402"),
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Str("402"), ctx.GetField("inclusionFlag"));
        // No exception; the extra question is just ignored.
    }

    [Fact]
    public void PupilAge_IsRead_FromPupilObject_WhenPositive()
    {
        var msg = NewMessage("Remove - year-group-change", pupilAge: 17);

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Num(17), ctx.GetField("pupilAge"));
    }

    [Fact]
    public void PupilAge_IsUnknown_WhenZero()
    {
        // 0 is the default for int — treat it as "not supplied" to keep Scrutiny defaults safe.
        var msg = NewMessage("Remove - year-group-change", pupilAge: 0);

        var ctx = _sut.Map(msg);

        Assert.IsType<FieldValue.Unknown>(ctx.GetField("pupilAge"));
    }

    [Fact]
    public void PupilAgeAnswer_OverridesPupilObject_WhenBothPresent()
    {
        var msg = NewMessage("Remove - year-group-change", pupilAge: 17, answers: new[]
        {
            Answer("pupil-age", "18")
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Num(18), ctx.GetField("pupilAge"));
    }

    [Fact]
    public void EnvelopeFields_AreAlwaysPopulated()
    {
        var msg = NewMessage("Remove - pupil-died", checkingWindowType: "KS4");

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Str("KS4"),                 ctx.GetField("keyStage"));
        Assert.Equal(new FieldValue.Str("Remove - pupil-died"), ctx.GetField("requestType"));
    }

    [Fact]
    public void Map_NullMessage_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Map(null!));
    }

    // --- helpers ---

    private static RequestDocument NewMessage(
        string whatToChange,
        string checkingWindowType = "KS4",
        IEnumerable<AnswerRecord>? answers = null,
        int pupilAge = 0) =>
        new()
        {
            ReferenceNumber    = "REF",
            WhatToChange       = whatToChange,
            CheckingWindowType = checkingWindowType,
            CheckingWindowId   = Guid.NewGuid(),
            SubmittedAt        = DateTime.UtcNow,
            SubmittedBy        = new UserDetails { UserId = "u", DisplayName = "x" },
            School             = new SchoolDetails { Urn = "1", Name = "S" },
            Pupil              = new PupilDetails
            {
                Id = "p", CypmdId = "c", Firstname = "A", Surname = "B",
                DateOfBirth = "01/01/2010", Sex = "F", Age = pupilAge, Upn = "UPN",
            },
            Answers            = (answers ?? Array.Empty<AnswerRecord>()).ToList(),
        };

    private static AnswerRecord Answer(string id, string value) =>
        new() { QuestionId = id, QuestionTitle = id, Type = "text", Value = value };
}
