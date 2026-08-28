using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.UnitTests.Notify;

public sealed class EmailSubstitutionsTests
{
    private static CheckingWindowDto Window(
        string? title,
        KeyStages keyStage = KeyStages.KS4,
        CheckingWindowType type = CheckingWindowType.KS4June,
        string turnaroundCommitment = "") =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = title ?? string.Empty,
            KeyStage = keyStage,
            CheckingWindowType = type,
            StartDate = new DateTime(2026, 6, 1),
            EndDate = new DateTime(2026, 6, 30, 17, 0, 0),
            TurnaroundCommitment = turnaroundCommitment
        };

    // ── CeName ───────────────────────────────────────────────────────────────

    [Fact]
    public void From_CeName_UsesWindowTitleWhenSet()
    {
        var result = EmailSubstitutions.From(Window("KS4 June"));

        Assert.Equal("KS4 June", result.CeName);
    }

    [Fact]
    public void From_CeName_FallsBackToWindowTypeDisplayName_WhenTitleIsWhitespace()
    {
        var result = EmailSubstitutions.From(Window("   ", type: CheckingWindowType.KS4June));

        Assert.Equal("Key Stage 4 June", result.CeName);
    }

    [Theory]
    [InlineData(CheckingWindowType.KS2, "Key Stage 2")]
    [InlineData(CheckingWindowType.KS4June, "Key Stage 4 June")]
    [InlineData(CheckingWindowType.KS4Autumn, "Key Stage 4 Autumn")]
    [InlineData(CheckingWindowType.Post16, "Post 16")]
    public void From_CeName_FallsBackToDisplayNameForEveryWindowType(
        CheckingWindowType type, string expected)
    {
        var result = EmailSubstitutions.From(Window("", type: type));

        Assert.Equal(expected, result.CeName);
    }

    // ── LearnerNoun ──────────────────────────────────────────────────────────

    [Fact]
    public void From_LearnerNoun_IsStudent_WhenKeyStageIsPost16()
    {
        var result = EmailSubstitutions.From(Window("16 to 19", keyStage: KeyStages.Post16));

        Assert.Equal("Student", result.LearnerNoun);
    }

    [Theory]
    [InlineData(KeyStages.KS2)]
    [InlineData(KeyStages.KS4)]
    public void From_LearnerNoun_IsPupil_WhenKeyStageIsNotPost16(KeyStages keyStage)
    {
        var result = EmailSubstitutions.From(Window("KS4 June", keyStage: keyStage));

        Assert.Equal("Pupil", result.LearnerNoun);
    }

    // ── TurnaroundCommitment ─────────────────────────────────────────────────

    [Fact]
    public void From_TurnaroundCommitment_PassesThroughConfiguredValueVerbatim()
    {
        var result = EmailSubstitutions.From(Window("KS4 June", turnaroundCommitment: "updated in the Autumn"));

        Assert.Equal("updated in the Autumn", result.TurnaroundCommitment);
    }

    [Fact]
    public void From_TurnaroundCommitment_IsEmpty_WhenNotConfigured()
    {
        var result = EmailSubstitutions.From(Window("KS4 June"));

        Assert.Equal(string.Empty, result.TurnaroundCommitment);
    }
}
