using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Validators;
using DfE.CheckPerformanceData.Domain.Enums;

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

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Question MakeQuestion(QuestionType type, string id = "q1", int? characterLimit = null,
        string? validator = null, bool optional = false) =>
        new() { Id = id, Type = type, Title = "My question", CharacterLimit = characterLimit, Validator = validator, Optional = optional };

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

    private static FileAnswer MakeFileAnswer(int pageCount) =>
        new() { StoredFileName = Guid.NewGuid().ToString(), OriginalFileName = "file.pdf", PageCount = pageCount };
}
