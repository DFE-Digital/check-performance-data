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
    public void Maps_KnownRequestTypeCode_ToOutcomeKey(string whatToChange, string expected)
    {
        var msg = NewMessage(whatToChange);

        var ctx = _sut.Map(msg);

        Assert.Equal(expected, ctx.OutcomeKey);
    }

    [Fact]
    public void Maps_UnknownRequestTypeCode_ToUnknownSentinel()
    {
        var msg = NewMessage("nonsense reason");

        var ctx = _sut.Map(msg);

        Assert.Equal(AnswerFieldMap.UnknownOutcomeKey, ctx.OutcomeKey);
    }

    [Fact]
    public void Maps_NullRequestTypeCode_ToUnknownSentinel()
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

    // --- Field projection: plain mapped questions ---

    [Theory]
    [InlineData("date-removed-from-roll",                 "dateOfRemoval")]
    [InlineData("date-permanently-excluded",              "dateOfPermanentExclusion")]
    [InlineData("date-pupil-excluded",                    "dateOfPermanentExclusion")]
    [InlineData("date-pupil-started",                     "schoolAdmissionDate")]
    [InlineData("date-pupil-started-school-in-england",   "firstSchoolAdmissionDate")]
    [InlineData("date-pupil-arrived-in-england",          "dateOfArrivalInEngland")]
    public void Maps_JourneyDateQuestion_ToCanonicalDateField(string questionId, string field)
    {
        var msg = NewMessage("Remove - elective-home-education", answers: new[]
        {
            Answer(questionId, "2025-02-15")
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Date(new DateOnly(2025, 2, 15)), ctx.GetField(field));
    }

    [Fact]
    public void Maps_CountryAnswer_ToCountryOfOrigin()
    {
        // The producer puts the autocomplete's ISO code in RawValue.
        var msg = NewMessage("Remove - english-not-first-language", answers: new[]
        {
            new AnswerRecord { QuestionId = "country-originally-from", QuestionTitle = "t", Type = "Autocomplete", Value = "France", RawValue = "FR" }
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Str("FR"), ctx.GetField("countryOfOrigin"));
    }

    [Fact]
    public void RawValue_TakesPrecedenceOverDisplayValue()
    {
        var msg = NewMessage("Remove - elective-home-education", answers: new[]
        {
            new AnswerRecord { QuestionId = "date-removed-from-roll", QuestionTitle = "t", Type = "Date", Value = "15/02/2025", RawValue = "2025-02-15" }
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Date(new DateOnly(2025, 2, 15)), ctx.GetField("dateOfRemoval"));
    }

    [Fact]
    public void Maps_IsoDateTimeAnswer_TruncatesToDate()
    {
        var msg = NewMessage("Remove - elective-home-education", answers: new[]
        {
            Answer("date-removed-from-roll", "2025-02-15T13:45:00Z")
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Date(new DateOnly(2025, 2, 15)), ctx.GetField("dateOfRemoval"));
    }

    [Fact]
    public void Throws_OnMalformedDate()
    {
        var msg = NewMessage("Remove - elective-home-education", answers: new[]
        {
            Answer("date-removed-from-roll", "not-a-date")
        });

        var ex = Assert.Throws<RuleContextMappingException>(() => _sut.Map(msg));
        Assert.Contains("dateOfRemoval", ex.Message);
    }

    [Fact]
    public void UnmappedQuestionId_IsSilentlyIgnored()
    {
        var msg = NewMessage("Remove - elective-home-education", answers: new[]
        {
            Answer("some-extra-question", "value"),
            Answer("date-removed-from-roll", "2025-02-15"),
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Date(new DateOnly(2025, 2, 15)), ctx.GetField("dateOfRemoval"));
        // No exception; the extra question is just ignored.
    }

    // --- Field projection: inclusionFlag from the pupil record ---

    [Fact]
    public void InclusionFlag_IsRead_FromPupilPincl()
    {
        var msg = NewMessage("Include", pupilPincl: 402);

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Str("402"), ctx.GetField("inclusionFlag"));
    }

    [Fact]
    public void InclusionFlag_IsUnknown_WhenPinclNotSupplied()
    {
        var msg = NewMessage("Include", pupilPincl: 0);

        var ctx = _sut.Map(msg);

        Assert.IsType<FieldValue.Unknown>(ctx.GetField("inclusionFlag"));
    }

    // --- Field projection: translated radio vocabularies ---

    [Theory]
    [InlineData("english",          "ENG", false)]
    [InlineData("believed-english", "ENG", true)]
    [InlineData("other",            "OTH", false)]
    [InlineData("believed-other",   "OTH", true)]
    public void Maps_FirstLanguage_ToEngOthVocabulary(string raw, string expected, bool uncertain)
    {
        var msg = NewMessage("Remove - english-not-first-language", answers: new[]
        {
            Answer("first-language", raw)
        });

        var ctx = _sut.Map(msg);

        FieldValue expectedValue = uncertain
            ? new FieldValue.Uncertain(new FieldValue.Str(expected))
            : new FieldValue.Str(expected);
        Assert.Equal(expectedValue, ctx.GetField("firstLanguage"));
    }

    [Theory]
    [InlineData("chose-not-to-say")]
    [InlineData("not-known")]
    [InlineData("hand-crafted-nonsense")]
    public void Maps_FirstLanguage_UnknownishValues_ToUnknown(string raw)
    {
        var msg = NewMessage("Remove - english-not-first-language", answers: new[]
        {
            Answer("first-language", raw)
        });

        var ctx = _sut.Map(msg);

        Assert.IsType<FieldValue.Unknown>(ctx.GetField("firstLanguage"));
    }

    [Theory]
    [InlineData("lower",  "Lower")]
    [InlineData("higher", "Higher")]
    public void Maps_HigherLower_ToYearGroupChange(string raw, string expected)
    {
        var msg = NewMessage("Remove - year-group-change", answers: new[]
        {
            Answer("higher-lower", raw)
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Str(expected), ctx.GetField("yearGroupChange"));
    }

    // --- Field projection: single-radio fan-out to booleans ---

    [Fact]
    public void SocialCareReason_FansOut_ToThreeBooleans()
    {
        var msg = NewMessage("Remove - social-care-involvement", answers: new[]
        {
            Answer("social-care-reason", "police-involvement")
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Bool(false), ctx.GetField("hadSocialCareInvolvement"));
        Assert.Equal(new FieldValue.Bool(true),  ctx.GetField("hadRecentPoliceInvolvement"));
        Assert.Equal(new FieldValue.Bool(false), ctx.GetField("hasBeenDetainedInPrison"));
    }

    [Fact]
    public void LifeLimitingIllnessHealthIssue_FansOut_ToIllnessBooleans()
    {
        var msg = NewMessage("Remove - life-limiting-illness", answers: new[]
        {
            Answer("life-limiting-illness-health-issue", "life-limiting")
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Bool(true),  ctx.GetField("hasTerminalIllness"));
        Assert.Equal(new FieldValue.Bool(false), ctx.GetField("hasCriticalIllness12mPlus"));
        Assert.Equal(new FieldValue.Bool(false), ctx.GetField("hasRecentLifeChangingDiagnosis"));
        Assert.Equal(new FieldValue.Bool(false), ctx.GetField("hasRecentLifeChangingInjury"));
        Assert.Equal(new FieldValue.Bool(false), ctx.GetField("underInvestigation12mPlus"));
        // No journey question collects this — stays Unknown so rules defer to Scrutiny.
        Assert.IsType<FieldValue.Unknown>(ctx.GetField("illnessHasSevereProfoundEffect"));
    }

    [Fact]
    public void FanOutAnswer_WithEmptyValue_LeavesFieldsUnknown()
    {
        var msg = NewMessage("Remove - social-care-involvement", answers: new[]
        {
            Answer("social-care-reason", "")
        });

        var ctx = _sut.Map(msg);

        Assert.IsType<FieldValue.Unknown>(ctx.GetField("hadSocialCareInvolvement"));
        Assert.IsType<FieldValue.Unknown>(ctx.GetField("hadRecentPoliceInvolvement"));
        Assert.IsType<FieldValue.Unknown>(ctx.GetField("hasBeenDetainedInPrison"));
    }

    // --- Field projection: sat-exams resolves by key stage ---

    [Theory]
    [InlineData("KS4", "yes", "hasSatExamsAsYear11", true)]
    [InlineData("KS4", "no",  "hasSatExamsAsYear11", false)]
    [InlineData("KS2", "yes", "hasSatExamsAsYear6",  true)]
    [InlineData("KS2", "no",  "hasSatExamsAsYear6",  false)]
    public void SatExams_MapsToKeyStageSpecificField(string keyStage, string raw, string field, bool expected)
    {
        var msg = NewMessage("Remove - social-care-involvement", checkingWindowType: keyStage, answers: new[]
        {
            Answer("sat-exams", raw)
        });

        var ctx = _sut.Map(msg);

        Assert.Equal(new FieldValue.Bool(expected), ctx.GetField(field));
    }

    [Fact]
    public void SatExams_OnUnrecognisedKeyStage_IsIgnored()
    {
        var msg = NewMessage("Remove - social-care-involvement", checkingWindowType: "Post16", answers: new[]
        {
            Answer("sat-exams", "yes")
        });

        var ctx = _sut.Map(msg);

        Assert.IsType<FieldValue.Unknown>(ctx.GetField("hasSatExamsAsYear11"));
        Assert.IsType<FieldValue.Unknown>(ctx.GetField("hasSatExamsAsYear6"));
    }

    // --- Field projection: error and empty handling ---

    [Fact]
    public void Throws_OnMalformedBool()
    {
        var msg = NewMessage("Remove - social-care-involvement", checkingWindowType: "KS4", answers: new[]
        {
            Answer("sat-exams", "maybe")
        });

        var ex = Assert.Throws<RuleContextMappingException>(() => _sut.Map(msg));
        Assert.Contains("hasSatExamsAsYear11", ex.Message);
    }

    [Fact]
    public void EmptyAnswerValue_BecomesUnknown()
    {
        var msg = NewMessage("Remove - elective-home-education", answers: new[]
        {
            Answer("date-removed-from-roll", "")
        });

        var ctx = _sut.Map(msg);

        Assert.IsType<FieldValue.Unknown>(ctx.GetField("dateOfRemoval"));
    }

    [Fact]
    public void MissingAnswer_BecomesUnknown()
    {
        var msg = NewMessage("Remove - life-limiting-illness", answers: Array.Empty<AnswerRecord>());

        var ctx = _sut.Map(msg);

        Assert.IsType<FieldValue.Unknown>(ctx.GetField("hasTerminalIllness"));
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
        int pupilAge = 0,
        int pupilPincl = 0) =>
        new()
        {
            ChangeRequestId    = Guid.NewGuid(),
            ReferenceNumber    = "REF",
            RequestTypeCode    = whatToChange,
            CheckingWindowType = checkingWindowType,
            CheckingWindowId   = Guid.NewGuid(),
            SubmittedAt        = DateTime.UtcNow,
            SubmittedBy        = new UserDetails { UserId = "u", DisplayName = "x" },
            School             = new SchoolDetails { Urn = "1", Name = "S" },
            Pupil              = new PupilDetails
            {
                Id = "p", CypmdId = "c", Firstname = "A", Surname = "B",
                DateOfBirth = "01/01/2010", Sex = "F", Age = pupilAge, Upn = "UPN",
                Pincl = pupilPincl,
            },
            Answers            = (answers ?? Array.Empty<AnswerRecord>()).ToList(),
        };

    private static AnswerRecord Answer(string id, string value) =>
        new() { QuestionId = id, QuestionTitle = id, Type = "text", Value = value, RawValue = value };
}
