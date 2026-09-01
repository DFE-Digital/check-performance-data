using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;
using DfE.CheckPerformanceData.Application.Journey.Validators;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class JourneyValidationServiceTests
{
    private readonly JourneyValidationService _sut = new();


    // ── ValidateAnswer ──────────────────────────────────────────────────────

    [Fact]
    public void ValidateAnswer_Radio_WhenAnswered_ReturnsNull()
    {
        var question = MakeQuestion(QuestionType.Radio);
        var answer = new QuestionAnswer { TextValue = "yes" };

        Assert.Null(_sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_Radio_WhenEmpty_ReturnsError()
    {
        var question = MakeQuestion(QuestionType.Radio);
        var answer = new QuestionAnswer { TextValue = "" };

        Assert.Equal("My question is required", _sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_FreeText_WhenAnswered_ReturnsNull()
    {
        var question = MakeQuestion(QuestionType.FreeText);
        var answer = new QuestionAnswer { TextValue = "some text" };

        Assert.Null(_sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_FreeText_WhenEmpty_ReturnsError()
    {
        var question = MakeQuestion(QuestionType.FreeText);
        var answer = new QuestionAnswer { TextValue = "   " };

        Assert.Equal("My question is required", _sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_TextArea_WhenAnswered_ReturnsNull()
    {
        var question = MakeQuestion(QuestionType.TextArea, characterLimit: 100);
        var answer = new QuestionAnswer { TextValue = "some text" };

        Assert.Null(_sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_TextArea_WhenEmpty_ReturnsError()
    {
        var question = MakeQuestion(QuestionType.TextArea);
        var answer = new QuestionAnswer { TextValue = null };

        Assert.Equal("My question is required", _sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_TextArea_WhenExceedsCharacterLimit_ReturnsError()
    {
        var question = MakeQuestion(QuestionType.TextArea, characterLimit: 10);
        var answer = new QuestionAnswer { TextValue = new string('x', 11) };

        Assert.Equal("My question must be 10 characters or less", _sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_TextArea_WhenAtCharacterLimit_ReturnsNull()
    {
        var question = MakeQuestion(QuestionType.TextArea, characterLimit: 10);
        var answer = new QuestionAnswer { TextValue = new string('x', 10) };

        Assert.Null(_sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_FreeText_WhenExceedsCharacterLimit_ReturnsError()
    {
        var question = MakeQuestion(QuestionType.FreeText, characterLimit: 150);
        var answer = new QuestionAnswer { TextValue = new string('a', 151) };

        Assert.Equal("My question must be 150 characters or less", _sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_FreeText_WhenAtCharacterLimit_ReturnsNull()
    {
        var question = MakeQuestion(QuestionType.FreeText, characterLimit: 150);
        var answer = new QuestionAnswer { TextValue = new string('a', 150) };

        Assert.Null(_sut.ValidateAnswer(question, answer, "My question"));
    }

    // A FreeText field whose form key never arrived (a hand-crafted POST, or a page whose input
    // was removed) reads back as a null TextValue. The character-limit arm runs before the
    // generic "is required" arm, so it must not measure the length of a value that isn't there —
    // the answer is missing, not too long.
    [Fact]
    public void ValidateAnswer_FreeText_WhenValueIsNullAndACharacterLimitIsSet_ReportsItAsRequired()
    {
        var question = MakeQuestion(QuestionType.FreeText, characterLimit: 150);
        var answer = new QuestionAnswer { TextValue = null };

        Assert.Equal("My question is required", _sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_FreeText_WhenValueIsNull_UsesTheQuestionsOwnValidationFailure()
    {
        var question = MakeQuestion(QuestionType.FreeText, characterLimit: 150);
        var answer = new QuestionAnswer { TextValue = null };

        Assert.Equal("Enter the pupil's first name",
            _sut.ValidateAnswer(question, answer, "First name", "Enter the pupil's first name"));
    }

    [Fact]
    public void ValidateAnswer_TextArea_WhenValueIsNullAndACharacterLimitIsSet_ReportsItAsRequired()
    {
        var question = MakeQuestion(QuestionType.TextArea, characterLimit: 1000);
        var answer = new QuestionAnswer { TextValue = null };

        Assert.Equal("My question is required", _sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_Date_WhenAllFieldsProvided_ReturnsNull()
    {
        var question = MakeQuestion(QuestionType.Date);
        var answer = new QuestionAnswer { DateValue = new DateAnswer { Day = 1, Month = 6, Year = 2026 } };

        Assert.Null(_sut.ValidateAnswer(question, answer, "Date of birth"));
    }

    [Fact]
    public void ValidateAnswer_Date_WhenDayMissing_ReturnsError()
    {
        var question = MakeQuestion(QuestionType.Date);
        var answer = new QuestionAnswer { DateValue = new DateAnswer { Day = 0, Month = 6, Year = 2026 } };

        Assert.Equal("Date of birth is required", _sut.ValidateAnswer(question, answer, "Date of birth"));
    }

    [Fact]
    public void ValidateAnswer_Date_WhenDateValueNull_ReturnsError()
    {
        var question = MakeQuestion(QuestionType.Date);
        var answer = new QuestionAnswer { DateValue = null };

        Assert.Equal("Date of birth is required", _sut.ValidateAnswer(question, answer, "Date of birth"));
    }

    [Theory]
    [InlineData(26)]    // "26" typed meaning 2026 — must not be accepted as year 0026
    [InlineData(1)]
    [InlineData(999)]
    [InlineData(10000)] // five-digit typo — must error, not throw in DaysInMonth
    public void ValidateAnswer_Date_WhenYearIsNotFourDigits_ReturnsError(int year)
    {
        var question = MakeQuestion(QuestionType.Date);
        var answer = new QuestionAnswer { DateValue = new DateAnswer { Day = 15, Month = 6, Year = year } };

        Assert.Equal("Date of birth must include a 4-digit year",
            _sut.ValidateAnswer(question, answer, "Date of birth"));
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(2026)]
    [InlineData(9999)]
    public void ValidateAnswer_Date_WhenYearIsFourDigits_ReturnsNull(int year)
    {
        var question = MakeQuestion(QuestionType.Date);
        var answer = new QuestionAnswer { DateValue = new DateAnswer { Day = 15, Month = 6, Year = year } };

        Assert.Null(_sut.ValidateAnswer(question, answer, "Date of birth"));
    }

    // ── ValidateAnswer — scoped invalid-date fallback (removal journeys) ─────
    //
    // The six removal journeys want the question's own "Enter the date …" wording for EVERY
    // invalid-date failure, not the generic "is required"/"4-digit year"/"real date" messages.
    // The fallback is scoped to the three removal date question ids so the excluded EAL page
    // and any generic date question keep their existing messages.

    private const string ConfiguredInvalidDateMessage =
        "Enter the date Alice Smith was removed from your school roll";

    [Theory]
    [InlineData(RemovalJourneyDateRules.DatePupilExcluded)]
    [InlineData(RemovalJourneyDateRules.DatePermanentlyExcluded)]
    [InlineData(RemovalJourneyDateRules.DateRemovedFromRoll)]
    public void ValidateAnswer_RemovalDateQuestion_BlankOrPartFilled_ReturnsConfiguredMessage(string questionId)
    {
        var question = MakeDateQuestion(questionId);

        Assert.Equal(ConfiguredInvalidDateMessage,
            _sut.ValidateAnswer(question, new QuestionAnswer { DateValue = null }, "Date of birth", ConfiguredInvalidDateMessage));
        Assert.Equal(ConfiguredInvalidDateMessage,
            _sut.ValidateAnswer(question, new QuestionAnswer { DateValue = new DateAnswer { Day = 0, Month = 6, Year = 2026 } }, "Date of birth", ConfiguredInvalidDateMessage));
    }

    [Theory]
    [InlineData(RemovalJourneyDateRules.DatePupilExcluded)]
    [InlineData(RemovalJourneyDateRules.DatePermanentlyExcluded)]
    [InlineData(RemovalJourneyDateRules.DateRemovedFromRoll)]
    public void ValidateAnswer_RemovalDateQuestion_NonFourDigitYear_ReturnsConfiguredMessage(string questionId)
    {
        var question = MakeDateQuestion(questionId);

        Assert.Equal(ConfiguredInvalidDateMessage,
            _sut.ValidateAnswer(question,
                new QuestionAnswer { DateValue = new DateAnswer { Day = 15, Month = 6, Year = 26 } },
                "Date of birth", ConfiguredInvalidDateMessage));
    }

    [Theory]
    [InlineData(RemovalJourneyDateRules.DatePupilExcluded)]
    [InlineData(RemovalJourneyDateRules.DatePermanentlyExcluded)]
    [InlineData(RemovalJourneyDateRules.DateRemovedFromRoll)]
    public void ValidateAnswer_RemovalDateQuestion_ImpossibleCalendarDate_ReturnsConfiguredMessage(string questionId)
    {
        var question = MakeDateQuestion(questionId);

        Assert.Equal(ConfiguredInvalidDateMessage,
            _sut.ValidateAnswer(question,
                new QuestionAnswer { DateValue = new DateAnswer { Day = 31, Month = 2, Year = 2026 } },
                "Date of birth", ConfiguredInvalidDateMessage));
    }

    [Fact]
    public void ValidateAnswer_NonRemovalDateQuestion_KeepsGenericMessages()
    {
        // The excluded EAL page and any generic date question (e.g. date of birth) are NOT in
        // RemovalDateQuestionIds, so only the blank case honours the configured message (as it
        // always has); the 4-digit-year and real-date failures stay generic.
        var question = MakeDateQuestion("date-of-birth");

        Assert.Equal(ConfiguredInvalidDateMessage,
            _sut.ValidateAnswer(question, new QuestionAnswer { DateValue = null }, "Date of birth", ConfiguredInvalidDateMessage));
        Assert.Equal("Date of birth must include a 4-digit year",
            _sut.ValidateAnswer(question,
                new QuestionAnswer { DateValue = new DateAnswer { Day = 15, Month = 6, Year = 26 } },
                "Date of birth", ConfiguredInvalidDateMessage));
        Assert.Equal("Date of birth must be a real date",
            _sut.ValidateAnswer(question,
                new QuestionAnswer { DateValue = new DateAnswer { Day = 31, Month = 2, Year = 2026 } },
                "Date of birth", ConfiguredInvalidDateMessage));
    }

    // ── ValidateAnswer with a named format validator ────────────────────────

    [Fact]
    public void ValidateAnswer_WhenNamedValidatorRejectsPresentValue_ReturnsFailureMessage()
    {
        var sut = new JourneyValidationService([new DfeNumberFormatValidator()]);
        var question = MakeQuestion(QuestionType.FreeText, validator: "DfeNumber");
        var answer = new QuestionAnswer { TextValue = "not-a-dfe-number" };

        Assert.Equal("Enter a DfE number in the format 123/4567 or 1234567",
            sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_WhenNamedValidatorAcceptsPresentValue_ReturnsNull()
    {
        var sut = new JourneyValidationService([new DfeNumberFormatValidator()]);
        var question = MakeQuestion(QuestionType.FreeText, validator: "DfeNumber");
        var answer = new QuestionAnswer { TextValue = "123/4567" };

        Assert.Null(sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_WhenValidatorSetButValueEmpty_SkipsFormatCheckAndReturnsRequiredMessage()
    {
        // The format validator would reject "" (IsValid("") is false); proving the
        // returned message is the required rule's, not the DfE format message,
        // confirms the format check is skipped on an empty value.
        var sut = new JourneyValidationService([new DfeNumberFormatValidator()]);
        var question = MakeQuestion(QuestionType.FreeText, validator: "DfeNumber");
        var answer = new QuestionAnswer { TextValue = "" };

        Assert.Equal("My question is required", sut.ValidateAnswer(question, answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_WhenValidatorNameIsUnregistered_SkipsFormatCheck()
    {
        var sut = new JourneyValidationService([new DfeNumberFormatValidator()]);
        var question = MakeQuestion(QuestionType.FreeText, validator: "NoSuchValidator");
        var answer = new QuestionAnswer { TextValue = "anything goes" };

        Assert.Null(sut.ValidateAnswer(question, answer, "My question"));
    }

    // ── IsAnswered ──────────────────────────────────────────────────────────

    [Fact]
    public void IsAnswered_FileUpload_WithFiles_ReturnsTrue()
    {
        var question = MakeQuestion(QuestionType.FileUpload);
        var answer = new QuestionAnswer { FileValues = [MakeFileAnswer(pageCount: 1)] };

        Assert.True(_sut.IsAnswered(question, answer));
    }

    [Fact]
    public void IsAnswered_FileUpload_WithNoFiles_ReturnsFalse()
    {
        var question = MakeQuestion(QuestionType.FileUpload);

        Assert.False(_sut.IsAnswered(question, new QuestionAnswer { FileValues = [] }));
        Assert.False(_sut.IsAnswered(question, null));
    }

    [Fact]
    public void IsAnswered_TextArea_WithText_ReturnsTrue()
    {
        var question = MakeQuestion(QuestionType.TextArea);

        Assert.True(_sut.IsAnswered(question, new QuestionAnswer { TextValue = "something" }));
    }

    [Fact]
    public void IsAnswered_TextArea_WhenBlank_ReturnsFalse()
    {
        var question = MakeQuestion(QuestionType.TextArea);

        Assert.False(_sut.IsAnswered(question, new QuestionAnswer { TextValue = "   " }));
        Assert.False(_sut.IsAnswered(question, null));
    }

    [Fact]
    public void IsAnswered_Date_WithCompleteDate_ReturnsTrue()
    {
        var question = MakeQuestion(QuestionType.Date);
        var answer = new QuestionAnswer { DateValue = new DateAnswer { Day = 1, Month = 6, Year = 2026 } };

        Assert.True(_sut.IsAnswered(question, answer));
    }

    [Fact]
    public void IsAnswered_Date_WithIncompleteDate_ReturnsFalse()
    {
        var question = MakeQuestion(QuestionType.Date);
        var answer = new QuestionAnswer { DateValue = new DateAnswer { Day = 0, Month = 6, Year = 2026 } };

        Assert.False(_sut.IsAnswered(question, answer));
    }

    // ── ValidateRequireAtLeastOne ───────────────────────────────────────────

    [Fact]
    public void ValidateRequireAtLeastOne_WhenFlagOff_ReturnsNull()
    {
        var page = MakeEvidencePage(requireAtLeastOne: false);

        Assert.Null(_sut.ValidateRequireAtLeastOne(page, new Dictionary<string, QuestionAnswer>(), "Sam Smith"));
    }

    [Fact]
    public void ValidateRequireAtLeastOne_WhenGateSaysInactive_ReturnsNull()
    {
        var page = MakeEvidencePage(requireAtLeastOne: true);

        Assert.Null(_sut.ValidateRequireAtLeastOne(
            page, new Dictionary<string, QuestionAnswer>(), "Sam Smith", requireAtLeastOneActive: false));
    }

    [Fact]
    public void ValidateRequireAtLeastOne_WhenGateSaysActive_ReturnsResult()
    {
        var page = MakeEvidencePage(requireAtLeastOne: false);

        Assert.NotNull(_sut.ValidateRequireAtLeastOne(
            page, new Dictionary<string, QuestionAnswer>(), "Sam Smith", requireAtLeastOneActive: true));
    }

    [Fact]
    public void ValidateRequireAtLeastOne_WhenNeitherAnswered_ReturnsResultWithBothFieldErrors()
    {
        var page = MakeEvidencePage(requireAtLeastOne: true);

        var result = _sut.ValidateRequireAtLeastOne(page, new Dictionary<string, QuestionAnswer>(), "Sam Smith");

        Assert.NotNull(result);
        Assert.Equal("You must answer at least one of these questions", result.SummaryMessage);
        Assert.Equal(2, result.FieldErrors.Count);
        Assert.Equal("Upload at least one file", result.FieldErrors["evidence"]);
        Assert.Equal("Explain how the evidence supports the change", result.FieldErrors["how-evidence-supports"]);
    }

    [Fact]
    public void ValidateRequireAtLeastOne_WhenFileOnly_ReturnsNull()
    {
        var page = MakeEvidencePage(requireAtLeastOne: true);
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["evidence"] = new() { FileValues = [MakeFileAnswer(pageCount: 1)] }
        };

        Assert.Null(_sut.ValidateRequireAtLeastOne(page, answers, "Sam Smith"));
    }

    [Fact]
    public void ValidateRequireAtLeastOne_WhenTextOnly_ReturnsNull()
    {
        var page = MakeEvidencePage(requireAtLeastOne: true);
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["how-evidence-supports"] = new() { TextValue = "Because of reasons" }
        };

        Assert.Null(_sut.ValidateRequireAtLeastOne(page, answers, "Sam Smith"));
    }

    [Fact]
    public void ValidateRequireAtLeastOne_WhenBothAnswered_ReturnsNull()
    {
        var page = MakeEvidencePage(requireAtLeastOne: true);
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["evidence"] = new() { FileValues = [MakeFileAnswer(pageCount: 1)] },
            ["how-evidence-supports"] = new() { TextValue = "Because of reasons" }
        };

        Assert.Null(_sut.ValidateRequireAtLeastOne(page, answers, "Sam Smith"));
    }

    [Fact]
    public void ValidateRequireAtLeastOne_ResolvesPupilNameInFieldError()
    {
        var page = new JourneyPage
        {
            Id = "p",
            Type = PageType.EvidenceUpload,
            RequireAtLeastOne = true,
            Questions =
            [
                new Question { Id = "evidence", Type = QuestionType.FileUpload, Title = "Upload files" },
                new Question { Id = "why", Type = QuestionType.TextArea, Title = "Explain why {pupilName} is affected" }
            ]
        };

        var result = _sut.ValidateRequireAtLeastOne(page, new Dictionary<string, QuestionAnswer>(), "Sam Smith");

        Assert.NotNull(result);
        Assert.Equal("Explain why Sam Smith is affected", result.FieldErrors["why"]);
    }

    // ── ValidateFileUpload ──────────────────────────────────────────────────

    [Fact]
    public void ValidateFileUpload_WhenUnderLimit_ReturnsNull()
    {
        var existing = new[] { MakeFileAnswer(pageCount: 2), MakeFileAnswer(pageCount: 2) };

        Assert.Null(_sut.ValidateFileUpload("evidence.pdf", 2, existing));
    }

    [Fact]
    public void ValidateFileUpload_WhenExactlyAtLimit_ReturnsNull()
    {
        var existing = new[] { MakeFileAnswer(pageCount: 5) };

        Assert.Null(_sut.ValidateFileUpload("evidence.pdf", 1, existing));
    }

    [Fact]
    public void ValidateFileUpload_WhenExceedsLimit_ReturnsError()
    {
        var sut = new JourneyValidationService(maxEvidencePages: 6);
        var existing = new[] { MakeFileAnswer(pageCount: 5) };

        var result = sut.ValidateFileUpload("evidence.pdf", 2, existing);

        Assert.NotNull(result);
        Assert.Contains("evidence.pdf", result);
        Assert.Contains("7 pages", result);
        Assert.Contains("6-page limit", result);
    }

    [Fact]
    public void ValidateFileUpload_PageCountSingular_UsesPageNotPages()
    {
        var sut = new JourneyValidationService(maxEvidencePages: 6);

        var result = sut.ValidateFileUpload("doc.pdf", 1, [MakeFileAnswer(pageCount: 6)]);

        Assert.NotNull(result);
        Assert.Contains("1 page", result);
        Assert.DoesNotContain("1 pages", result);
    }

    // ── ValidateDuplicateFileName (AB#296081) ───────────────────────────────
    // A request must never hold two evidence files with the same name — storage cost
    // and user confusion. Comparison is on OriginalFileName (what the user sees in the
    // uploaded-files table), case-insensitive because file systems the user comes from
    // treat "Evidence.PDF" and "evidence.pdf" as the same file.

    [Fact]
    public void ValidateDuplicateFileName_WhenNameAlreadyUsed_ReturnsTicketWording()
    {
        var existing = new[] { MakeFileAnswer(originalFileName: "evidence.pdf") };

        var result = _sut.ValidateDuplicateFileName("evidence.pdf", existing);

        Assert.Equal(
            "The file name has already been used. Upload a file with a different name.",
            result);
    }

    [Fact]
    public void ValidateDuplicateFileName_IsCaseInsensitive()
    {
        var existing = new[] { MakeFileAnswer(originalFileName: "Evidence.PDF") };

        Assert.NotNull(_sut.ValidateDuplicateFileName("evidence.pdf", existing));
    }

    [Fact]
    public void ValidateDuplicateFileName_WhenNameIsNew_ReturnsNull()
    {
        var existing = new[] { MakeFileAnswer(originalFileName: "evidence.pdf") };

        Assert.Null(_sut.ValidateDuplicateFileName("other.pdf", existing));
    }

    [Fact]
    public void ValidateDuplicateFileName_WhenNoFilesYet_ReturnsNull()
    {
        Assert.Null(_sut.ValidateDuplicateFileName("evidence.pdf", []));
    }

    // ── GenerateReference ───────────────────────────────────────────────────

    [Fact]
    public void GenerateReference_IncludesWindowType()
    {
        var reference = _sut.GenerateReference(CheckingWindowType.KS4June);

        Assert.StartsWith("CYPMD_KS4June_", reference);
    }

    [Fact]
    public void GenerateReference_WhenWindowTypeNull_UsesUnknown()
    {
        var reference = _sut.GenerateReference(null);

        Assert.StartsWith("CYPMD_Unknown_", reference);
    }

    [Fact]
    public void GenerateReference_IsUnique()
    {
        var a = _sut.GenerateReference(CheckingWindowType.KS2);
        var b = _sut.GenerateReference(CheckingWindowType.KS2);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GenerateReference_HasExpectedFormat()
    {
        var reference = _sut.GenerateReference(CheckingWindowType.KS2);
        var parts = reference.Split('_');

        Assert.Equal(3, parts.Length);
        Assert.Equal("CYPMD", parts[0]);
        Assert.Equal("KS2", parts[1]);
        Assert.Equal(7, parts[2].Length);
        Assert.All(parts[2], c => Assert.True(char.IsLetterOrDigit(c)));
    }

    // ── ValidateEvidencePage ────────────────────────────────────────────────

    [Fact]
    public void ValidateEvidencePage_WhenRequiredFileMissing_ReturnsMessage()
    {
        var page = new JourneyPage
        {
            Id = "evidence",
            Type = PageType.EvidenceUpload,
            Questions = [new Question { Id = "evidence", Type = QuestionType.FileUpload, Title = "Upload evidence" }]
        };
        var journey = new RequestState();

        var result = _sut.ValidateEvidencePage(page, journey, "Jane Smith");

        Assert.NotNull(result);
        Assert.Contains("Upload at least one file before continuing", result!.Messages);
    }

    [Fact]
    public void ValidateEvidencePage_WhenRequiredQuestionMissing_UsesConfiguredValidationFailure()
    {
        var page = new JourneyPage
        {
            Id = "evidence",
            Type = PageType.EvidenceUpload,
            Questions =
            [
                new Question
                {
                    Id = "how-evidence-supports",
                    Type = QuestionType.TextArea,
                    Title = "How does the evidence demonstrate why the pupil should be removed?",
                    ValidationFailure = "Explain how the evidence supports removing {pupilName}"
                }
            ]
        };
        var journey = new RequestState();

        var result = _sut.ValidateEvidencePage(page, journey, "Jane Smith");

        Assert.NotNull(result);
        Assert.Contains("Explain how the evidence supports removing Jane Smith", result!.Messages);
    }

    [Fact]
    public void ValidateEvidencePage_WhenFilePresent_ReturnsNull()
    {
        var page = new JourneyPage
        {
            Id = "evidence",
            Type = PageType.EvidenceUpload,
            Questions = [new Question { Id = "evidence", Type = QuestionType.FileUpload, Title = "Upload evidence" }]
        };
        var journey = new RequestState();
        journey.QuestionAnswers["evidence"] = new QuestionAnswer
        {
            FileValues = [new FileAnswer
            {
                StoredFileName = "abc",
                OriginalFileName = "abc.pdf",
                PageCount = 1,
                FileSizeBytes = 100
            }]
        };

        var result = _sut.ValidateEvidencePage(page, journey, "Jane Smith");

        Assert.Null(result);
    }

    [Fact]
    public void ValidateEvidencePage_ConditionallyOptionalQuestions_NoMessagesWhenEmpty()
    {
        var page = new JourneyPage
        {
            Id = "evidence",
            Type = PageType.EvidenceUpload,
            Questions =
            [
                new Question { Id = "evidence", Type = QuestionType.FileUpload, Title = "Upload evidence" },
                new Question
                {
                    Id = "how-evidence-supports",
                    Type = QuestionType.TextArea,
                    Title = "How does the evidence demonstrate why the pupil should be removed?",
                    ValidationFailure = "Explain how the evidence supports removing {pupilName}"
                }
            ]
        };
        var journey = new RequestState();

        var result = _sut.ValidateEvidencePage(page, journey, "Jane Smith",
            new HashSet<string> { "evidence", "how-evidence-supports" });

        Assert.Null(result);
    }

    [Fact]
    public void ValidateEvidencePage_WhenRequireAtLeastOneUnmet_ReturnsSummaryMessage()
    {
        var page = new JourneyPage
        {
            Id = "evidence",
            Type = PageType.EvidenceUpload,
            RequireAtLeastOne = true,
            Questions =
            [
                new Question { Id = "file", Type = QuestionType.FileUpload, Title = "Upload", Optional = true },
                new Question { Id = "explain", Type = QuestionType.TextArea, Title = "Explain", Optional = true }
            ]
        };
        var journey = new RequestState();

        var result = _sut.ValidateEvidencePage(page, journey, "Jane Smith");

        Assert.NotNull(result);
        Assert.Contains("You must answer at least one of these questions", result!.Messages);
    }

    // ── ValidatePageDates ───────────────────────────────────────────────────
    //
    // The rules themselves are covered by PageDateRulesTests. These cover the wiring: which
    // pages the rules apply to, how answers are turned into comparable dates, and which
    // calendar "today" comes from.

    [Fact]
    public void ValidatePageDates_PageWithoutDateRules_ReturnsEmpty()
    {
        var page = MakeEvidencePage(requireAtLeastOne: false);

        Assert.Empty(_sut.ValidatePageDates(page, new Dictionary<string, QuestionAnswer>(), "Jane Smith"));
    }

    [Fact]
    public void ValidatePageDates_EalDetailsPage_AppliesTheRules()
    {
        var sut = DateRuleSut("2026-06-15T12:00:00Z");
        var answers = EalAnswers(
            started: new DateAnswer { Day = 4, Month = 9, Year = 2023 },
            firstEnglishSchool: new DateAnswer { Day = 8, Month = 1, Year = 2024 });

        var violations = sut.ValidatePageDates(MakeEalDetailsPage(), answers, "Jane Smith");

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, v => v.QuestionId == PageDateRules.StartedAtSchool);
        Assert.Contains(violations, v => v.QuestionId == PageDateRules.FirstEnglishSchool);
    }

    [Fact]
    public void ValidatePageDates_ConsistentAnswers_ReturnsEmpty()
    {
        var sut = DateRuleSut("2026-06-15T12:00:00Z");
        var answers = EalAnswers(
            started: new DateAnswer { Day = 2, Month = 9, Year = 2024 },
            firstEnglishSchool: new DateAnswer { Day = 4, Month = 9, Year = 2023 },
            arrived: new DateAnswer { Day = 20, Month = 8, Year = 2023 });

        Assert.Empty(sut.ValidatePageDates(MakeEalDetailsPage(), answers, "Jane Smith"));
    }

    [Fact]
    public void ValidatePageDates_IncompleteDate_IsExcludedFromComparisons()
    {
        // A part-filled optional date is stored with the missing part as 0. It is not a date the
        // user can be told to reconcile with anything, so no rule involving it may fire.
        var sut = DateRuleSut("2026-06-15T12:00:00Z");
        var answers = EalAnswers(
            started: new DateAnswer { Day = 2, Month = 9, Year = 2024 },
            firstEnglishSchool: new DateAnswer { Day = 4, Month = 9, Year = 2023 },
            arrived: new DateAnswer { Day = 20, Month = 0, Year = 2025 });

        Assert.Empty(sut.ValidatePageDates(MakeEalDetailsPage(), answers, "Jane Smith"));
    }

    [Fact]
    public void ValidatePageDates_UnansweredQuestion_IsExcludedFromComparisons()
    {
        var sut = DateRuleSut("2026-06-15T12:00:00Z");
        var answers = EalAnswers(started: new DateAnswer { Day = 2, Month = 9, Year = 2024 });

        Assert.Empty(sut.ValidatePageDates(MakeEalDetailsPage(), answers, "Jane Smith"));
    }

    [Fact]
    public void ValidatePageDates_UsesTheUkCalendarDate_NotUtc()
    {
        // 23:30 UTC on 15 June is 00:30 BST on the 16th. A school completing the form just after
        // midnight must be able to enter today's UK date without it being called a future date.
        var sut = DateRuleSut("2026-06-15T23:30:00Z");
        var answers = EalAnswers(
            started: new DateAnswer { Day = 16, Month = 6, Year = 2026 },
            firstEnglishSchool: new DateAnswer { Day = 16, Month = 6, Year = 2026 });

        Assert.Empty(sut.ValidatePageDates(MakeEalDetailsPage(), answers, "Jane Smith"));
    }

    [Fact]
    public void ValidatePageDates_ResolvesThePupilNameInMessages()
    {
        var sut = DateRuleSut("2026-06-15T12:00:00Z");
        var answers = EalAnswers(
            started: new DateAnswer { Day = 16, Month = 6, Year = 2027 },
            firstEnglishSchool: new DateAnswer { Day = 4, Month = 9, Year = 2023 });

        var violation = Assert.Single(sut.ValidatePageDates(MakeEalDetailsPage(), answers, "Jane Smith"));

        Assert.Equal("The date Jane Smith started at your school must be in the past", violation.Message);
        Assert.DoesNotContain("{pupilName}", violation.Message, StringComparison.Ordinal);
    }

    // Each of the six removal journeys has a single removal/exclusion date that must not be in
    // the future. These cover the dispatch (which pages route to RemovalJourneyDateRules, and
    // which wording each carries); the rule itself is covered by RemovalJourneyDateRulesTests.

    [Theory]
    [InlineData(RemovalJourneyDateRules.PermanentExclusionPageId, RemovalJourneyDateRules.DatePupilExcluded,
        "Date Jane Smith was excluded must be in the past")]
    [InlineData(RemovalJourneyDateRules.ChildMissingEducationPageId, RemovalJourneyDateRules.DateRemovedFromRoll,
        "Date Jane Smith was removed from your school roll must be in the past")]
    [InlineData(RemovalJourneyDateRules.PupilDiedPageId, RemovalJourneyDateRules.DateRemovedFromRoll,
        "Date Jane Smith was removed from your school roll must be in the past")]
    [InlineData(RemovalJourneyDateRules.ElectiveHomeEducationPageId, RemovalJourneyDateRules.DateRemovedFromRoll,
        "Date Jane Smith was removed from your school roll must be in the past")]
    [InlineData(RemovalJourneyDateRules.PermanentlyExcludedPageId, RemovalJourneyDateRules.DatePermanentlyExcluded,
        "Date Jane Smith was permanently excluded from your school must be in the past")]
    [InlineData(RemovalJourneyDateRules.PermanentlyLeftEnglandPageId, RemovalJourneyDateRules.DateRemovedFromRoll,
        "Date Jane Smith was removed from your school roll must be in the past")]
    public void ValidatePageDates_RemovalPages_ApplyTheFutureDateRule(
        string pageId, string questionId, string expectedMessage)
    {
        var sut = DateRuleSut("2026-06-15T12:00:00Z");
        var answers = RemovalAnswers(questionId, new DateAnswer { Day = 16, Month = 6, Year = 2026 });

        var violation = Assert.Single(sut.ValidatePageDates(MakeRemovalPage(pageId, questionId), answers, "Jane Smith"));

        Assert.Equal(questionId, violation.QuestionId);
        Assert.Equal(expectedMessage, violation.Message);
    }

    [Theory]
    [InlineData(RemovalJourneyDateRules.PermanentExclusionPageId, RemovalJourneyDateRules.DatePupilExcluded)]
    [InlineData(RemovalJourneyDateRules.ChildMissingEducationPageId, RemovalJourneyDateRules.DateRemovedFromRoll)]
    [InlineData(RemovalJourneyDateRules.PupilDiedPageId, RemovalJourneyDateRules.DateRemovedFromRoll)]
    [InlineData(RemovalJourneyDateRules.ElectiveHomeEducationPageId, RemovalJourneyDateRules.DateRemovedFromRoll)]
    [InlineData(RemovalJourneyDateRules.PermanentlyExcludedPageId, RemovalJourneyDateRules.DatePermanentlyExcluded)]
    [InlineData(RemovalJourneyDateRules.PermanentlyLeftEnglandPageId, RemovalJourneyDateRules.DateRemovedFromRoll)]
    public void ValidatePageDates_RemovalPages_TodayIsAcceptable(string pageId, string questionId)
    {
        // "Later than today", not "before today" — a pupil removed from the roll today is valid.
        var sut = DateRuleSut("2026-06-15T12:00:00Z");
        var answers = RemovalAnswers(questionId, new DateAnswer { Day = 15, Month = 6, Year = 2026 });

        Assert.Empty(sut.ValidatePageDates(MakeRemovalPage(pageId, questionId), answers, "Jane Smith"));
    }

    [Fact]
    public void ValidatePageDates_RemovalPages_DoNotRunTheEalCrossFieldRules()
    {
        // A removal page must not be mistaken for the EAL page: no cross-field sequence rules
        // exist there, and the future-date wording differs (no leading "The").
        var sut = DateRuleSut("2026-06-15T12:00:00Z");
        var answers = RemovalAnswers(
            RemovalJourneyDateRules.DateRemovedFromRoll, new DateAnswer { Day = 16, Month = 6, Year = 2026 });

        var violations = sut.ValidatePageDates(
            MakeRemovalPage(RemovalJourneyDateRules.PupilDiedPageId, RemovalJourneyDateRules.DateRemovedFromRoll),
            answers, "Jane Smith");

        var violation = Assert.Single(violations);
        Assert.Equal(
            "Date Jane Smith was removed from your school roll must be in the past",
            violation.Message);
    }

    // ── DI activation ───────────────────────────────────────────────────────
    //
    // Every constructor parameter is optional and one of them (maxEvidencePages) can never be
    // resolved from the container, so the service is only constructible because the DI
    // container falls back to declared defaults. Adding a parameter is therefore riskier than
    // it looks, and nothing else covers it: no integration test boots the journey graph.

    [Fact]
    public void IsResolvable_FromTheContainer_AsRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IJourneyValidationService, JourneyValidationService>();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IJourneyValidationService>());
    }

    [Fact]
    public void IsResolvable_WhenNoTimeProviderIsRegistered()
    {
        // The worker composes a narrower service graph than the web host. Falling back to the
        // system clock keeps the date rules working rather than failing activation.
        var services = new ServiceCollection();
        services.AddScoped<IJourneyValidationService, JourneyValidationService>();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IJourneyValidationService>());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static JourneyValidationService DateRuleSut(string utcNow) =>
        new(timeProvider: new FakeTimeProvider(DateTimeOffset.Parse(utcNow)));

    private static Dictionary<string, QuestionAnswer> EalAnswers(
        DateAnswer? started = null, DateAnswer? firstEnglishSchool = null, DateAnswer? arrived = null)
    {
        var answers = new Dictionary<string, QuestionAnswer>();
        if (started is not null)
            answers[PageDateRules.StartedAtSchool] = new QuestionAnswer { DateValue = started };
        if (firstEnglishSchool is not null)
            answers[PageDateRules.FirstEnglishSchool] = new QuestionAnswer { DateValue = firstEnglishSchool };
        if (arrived is not null)
            answers[PageDateRules.ArrivedInEngland] = new QuestionAnswer { DateValue = arrived };
        return answers;
    }

    private static JourneyPage MakeEalDetailsPage() =>
        new()
        {
            Id = PageDateRules.EalDetailsPageId,
            Questions =
            [
                new Question { Id = PageDateRules.StartedAtSchool, Type = QuestionType.Date, Title = "Started" },
                new Question { Id = PageDateRules.FirstEnglishSchool, Type = QuestionType.Date, Title = "First English school" },
                new Question { Id = PageDateRules.ArrivedInEngland, Type = QuestionType.Date, Title = "Arrived", Optional = true }
            ]
        };

    private static Dictionary<string, QuestionAnswer> RemovalAnswers(string questionId, DateAnswer date) =>
        new() { [questionId] = new QuestionAnswer { DateValue = date } };

    private static JourneyPage MakeRemovalPage(string pageId, string questionId) =>
        new()
        {
            Id = pageId,
            Questions = [new Question { Id = questionId, Type = QuestionType.Date, Title = "Date" }]
        };

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Question MakeQuestion(QuestionType type, string id = "q1", int? characterLimit = null,
        string? validator = null, bool optional = false) =>
        new() { Id = id, Type = type, Title = "My question", CharacterLimit = characterLimit, Validator = validator, Optional = optional };

    private static Question MakeDateQuestion(string id) =>
        new() { Id = id, Type = QuestionType.Date, Title = "Date" };

    private static JourneyPage MakeEvidencePage(bool requireAtLeastOne) =>
        new()
        {
            Id = "evidence",
            Type = PageType.EvidenceUpload,
            RequireAtLeastOne = requireAtLeastOne,
            Questions =
            [
                new Question { Id = "evidence", Type = QuestionType.FileUpload, Title = "Upload files" },
                new Question
                {
                    Id = "how-evidence-supports",
                    Type = QuestionType.TextArea,
                    Title = "Explain how the evidence supports the change",
                    CharacterLimit = 1000
                }
            ]
        };

    private static FileAnswer MakeFileAnswer(int pageCount = 1, string originalFileName = "file.pdf") =>
        new() { StoredFileName = Guid.NewGuid().ToString(), OriginalFileName = originalFileName, PageCount = pageCount };
}
