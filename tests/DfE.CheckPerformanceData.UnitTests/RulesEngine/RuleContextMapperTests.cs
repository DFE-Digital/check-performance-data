using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.QueueMessages;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

public sealed class RuleContextMapperTests
{
    private readonly IRuleContextMapper _sut = new RuleContextMapper();

    // --- OutcomeKey ---

    [Theory]
    [InlineData("Deceased",                                                   "Deceased")]
    [InlineData("Elective home education",                                    "ElectiveHomeEducation")]
    [InlineData("  Elective home education  ",                                "ElectiveHomeEducation")]
    [InlineData("elective home education",                                    "ElectiveHomeEducation")] // case-insensitive
    [InlineData("Permanently left England",                                   "PermanentlyLeftEngland")]
    [InlineData("Social care involvement - including police/prison",          "SocialCareInvolvement")]
    [InlineData("Social care involvement including police/prison",            "SocialCareInvolvement")] // dash-less variant
    [InlineData("Pupil missing in Education",                                 "PupilMissingInEducation")]
    [InlineData("Not at end of 16 to 18 study",                               "NotAtEndOf16To18Study")]
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
        var msg = NewMessage("Deceased", checkingWindowType: raw);

        var ctx = _sut.Map(msg);

        Assert.Equal(expected, ctx.KeyStage);
    }

    // --- Field projection ---

    [Fact]
    public void Maps_StringAnswer_ToFieldValueStr()
    {
        var msg = NewMessage("Inclusion", answers: new[]
        {
            Answer("inclusion-status-flag", "402")
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Str("402"), ctx.GetField("inclusionFlag"));
    }

    [Fact]
    public void Maps_BoolAnswer_ToFieldValueBool()
    {
        var msg = NewMessage("Terminal/Critical illness", answers: new[]
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
        var msg = NewMessage("Elective home education", answers: new[]
        {
            Answer("date-of-removal-from-roll", "2025-02-15")
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Date(new DateOnly(2025, 2, 15)), ctx.GetField("dateOfRemoval"));
    }

    [Fact]
    public void Maps_IsoDateTimeAnswer_TruncatesToDate()
    {
        var msg = NewMessage("Elective home education", answers: new[]
        {
            Answer("date-of-removal-from-roll", "2025-02-15T13:45:00Z")
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Date(new DateOnly(2025, 2, 15)), ctx.GetField("dateOfRemoval"));
    }

    [Fact]
    public void Throws_OnMalformedDate()
    {
        var msg = NewMessage("Elective home education", answers: new[]
        {
            Answer("date-of-removal-from-roll", "not-a-date")
        });

        var ex = Assert.Throws<RuleContextMappingException>(() => _sut.Map(msg));
        Assert.Contains("dateOfRemoval", ex.Message);
    }

    [Fact]
    public void Throws_OnMalformedBool()
    {
        var msg = NewMessage("Terminal/Critical illness", answers: new[]
        {
            Answer("terminal-illness", "maybe")
        });

        var ex = Assert.Throws<RuleContextMappingException>(() => _sut.Map(msg));
        Assert.Contains("hasTerminalIllness", ex.Message);
    }

    [Fact]
    public void EmptyAnswerValue_BecomesUnknown()
    {
        var msg = NewMessage("Terminal/Critical illness", answers: new[]
        {
            Answer("terminal-illness", "")
        });

        var ctx = _sut.Map(msg);

        Assert.IsType<FieldValue.Unknown>(ctx.GetField("hasTerminalIllness"));
    }

    [Fact]
    public void MissingAnswer_BecomesUnknown()
    {
        var msg = NewMessage("Terminal/Critical illness", answers: Array.Empty<Answer>());

        var ctx = _sut.Map(msg);

        Assert.IsType<FieldValue.Unknown>(ctx.GetField("hasTerminalIllness"));
    }

    [Fact]
    public void UnmappedQuestionId_IsSilentlyIgnored()
    {
        var msg = NewMessage("Inclusion", answers: new[]
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
        var msg = NewMessage("Not at end of 16 to 18 study", pupilAge: 17);

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Num(17), ctx.GetField("pupilAge"));
    }

    [Fact]
    public void PupilAge_IsUnknown_WhenZero()
    {
        // 0 is the default for int — treat it as "not supplied" to keep Scrutiny defaults safe.
        var msg = NewMessage("Not at end of 16 to 18 study", pupilAge: 0);

        var ctx = _sut.Map(msg);

        Assert.IsType<FieldValue.Unknown>(ctx.GetField("pupilAge"));
    }

    [Fact]
    public void PupilAgeAnswer_OverridesPupilObject_WhenBothPresent()
    {
        var msg = NewMessage("Not at end of 16 to 18 study", pupilAge: 17, answers: new[]
        {
            Answer("pupil-age", "18")
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Num(18), ctx.GetField("pupilAge"));
    }

    [Fact]
    public void EnvelopeFields_AreAlwaysPopulated()
    {
        var msg = NewMessage("Deceased", checkingWindowType: "KS4");

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Str("KS4"),     ctx.GetField("keyStage"));
        Assert.Equal(new FieldValue.Str("Deceased"), ctx.GetField("requestType"));
    }

    [Fact]
    public void Map_NullMessage_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Map(null!));
    }

    // --- helpers ---

    private static RequestMessage NewMessage(
        string whatToChange,
        string checkingWindowType = "KS4",
        IEnumerable<Answer>? answers = null,
        int pupilAge = 0)
    {
        var msg = new TestMessage
        {
            WhatToChange       = whatToChange,
            CheckingWindowType = checkingWindowType,
            CheckingWindowId   = Guid.NewGuid(),
            Pupil              = new Pupil { Age = pupilAge },
            Answers            = (answers ?? Array.Empty<Answer>()).ToList(),
        };
        return msg;
    }

    private static Answer Answer(string id, string value) =>
        new() { QuestionId = id, QuestionTitle = id, Type = "text", Value = value };

    /// <summary>
    /// Minimal concrete <see cref="RequestMessage"/> for tests. The abstract
    /// <see cref="RequestMessage.ProcessAsync"/> is a no-op since the mapper
    /// never invokes it.
    /// </summary>
    private sealed class TestMessage : RequestMessage
    {
        public override Task ProcessAsync(CancellationToken token) => Task.CompletedTask;
    }
}
