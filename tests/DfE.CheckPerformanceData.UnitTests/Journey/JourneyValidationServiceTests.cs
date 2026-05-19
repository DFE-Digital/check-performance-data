using DfE.CheckPerformanceData.Application.Journey;
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
        var existing = new[] { MakeFileAnswer(pageCount: 5) };

        var result = _sut.ValidateFileUpload("evidence.pdf", 2, existing);

        Assert.NotNull(result);
        Assert.Contains("evidence.pdf", result);
        Assert.Contains("7 pages", result);
        Assert.Contains("6-page limit", result);
    }

    [Fact]
    public void ValidateFileUpload_PageCountSingular_UsesPageNotPages()
    {
        var result = _sut.ValidateFileUpload("doc.pdf", 1, [MakeFileAnswer(pageCount: 6)]);

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

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Question MakeQuestion(QuestionType type, string id = "q1", int? characterLimit = null) =>
        new() { Id = id, Type = type, Title = "My question", CharacterLimit = characterLimit };

    private static FileAnswer MakeFileAnswer(int pageCount) =>
        new() { StoredFileName = Guid.NewGuid().ToString(), OriginalFileName = "file.pdf", PageCount = pageCount };
}
