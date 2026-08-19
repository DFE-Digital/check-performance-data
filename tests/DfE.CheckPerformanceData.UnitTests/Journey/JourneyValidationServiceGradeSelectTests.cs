using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#296648 / AB#297130: the revised-grade rules. All four are server-authoritative — the picker is
// built from the same reference data, but a posted value is never trusted.
//
// The BTEC fixture is the ticket's own example. The IB Diploma fixture is the case that makes
// ordinal, case-sensitive comparison load-bearing: 24F is a fail and 24D is a pass, so a comparison
// that normalised or truncated would let the user "change" a grade to itself.
public sealed class JourneyValidationServiceGradeSelectTests
{
    private readonly JourneyValidationService _sut = new();

    private static readonly Question RevisedGrade = new()
    {
        Id = "q-revised-grade",
        Type = QuestionType.GradeSelect,
        Title = "What should the revised grade be?",
        ValidationFailure = "Select the revised grade"
    };

    private const string Required = "Select the revised grade";
    private const string MustDiffer = "The revised grade must be different from the current grade";

    private static readonly GradeReference Btec = new()
    {
        Qan = "60370683",
        QualificationTitle = "Pearson BTEC L1/L2 Tech Award in Sport",
        AwardingOrganisation = "Pearson",
        PassGrades = ["*2", "P1", "P2", "M1", "M2", "D1", "D2"],
        FailGrades = ["F", "Q", "R", "U", "X"]
    };

    private static readonly GradeReference Ib = new()
    {
        Qan = "50034157",
        QualificationTitle = "IBO Level 3 International Baccalaureate Diploma",
        AwardingOrganisation = "IBO",
        PassGrades = ["24B", "24D", "25B", "25D"],
        FailGrades = ["24F", "25F", "R", "U", "X"]
    };

    private string? Validate(string? chosen, GradeReference? reference, string? currentGrade) =>
        _sut.ValidateGradeSelect(
            RevisedGrade,
            chosen is null ? null : new QuestionAnswer { TextValue = chosen },
            reference,
            currentGrade,
            RevisedGrade.ValidationFailure);

    // ── 1. Unanswered ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unanswered_grade_is_required(string? chosen)
        => Assert.Equal(Required, Validate(chosen, Btec, "M1"));

    [Fact]
    public void The_required_message_falls_back_when_the_flow_config_supplies_none()
    {
        var error = _sut.ValidateGradeSelect(
            RevisedGrade, null, Btec, "M1", resolvedValidationFailure: null);

        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // ── 2. Same as the current grade ─────────────────────────────────────────

    [Fact]
    public void The_current_grade_cannot_be_the_revised_grade()
    {
        // "A revised grade that matches the current grade reports no change."
        Assert.Equal(MustDiffer, Validate("M1", Btec, "M1"));
    }

    [Fact]
    public void The_must_differ_check_is_case_sensitive()
    {
        // Grades are opaque codes. Treating "m1" as "M1" would be guessing at the user's intent, and
        // no qualification in the reference data uses case to distinguish grades anyway — so a
        // differently-cased value is simply not a grade this qualification offers.
        Assert.Equal(Required, Validate("m1", Btec, "M1"));
    }

    [Fact]
    public void Surrounding_whitespace_does_not_defeat_the_must_differ_check()
        => Assert.Equal(MustDiffer, Validate("  M1  ", Btec, "M1"));

    [Fact]
    public void A_different_grade_from_the_same_scale_passes()
        => Assert.Null(Validate("D1", Btec, "M1"));

    [Fact]
    public void A_fail_grade_is_a_valid_revised_grade()
    {
        // "A grade can be wrong in either direction, so both are needed."
        Assert.Null(Validate("U", Btec, "M1"));
    }

    [Fact]
    public void A_pass_grade_is_a_valid_revision_of_a_fail_grade()
        => Assert.Null(Validate("D2", Btec, "U"));

    // ── The prefix-sharing case ──────────────────────────────────────────────

    [Fact]
    public void Grades_sharing_a_prefix_are_treated_as_distinct()
    {
        // 24F (fail) and 24D (pass) differ only by suffix. Changing 24F to 24D is a real enquiry and
        // must be accepted; a sloppier comparison would reject it as "no change".
        Assert.Null(Validate("24D", Ib, "24F"));
        Assert.Null(Validate("24F", Ib, "24D"));
    }

    [Fact]
    public void The_same_prefix_sharing_grade_is_still_rejected()
    {
        Assert.Equal(MustDiffer, Validate("24F", Ib, "24F"));
        Assert.Equal(MustDiffer, Validate("24D", Ib, "24D"));
    }

    // ── 3. Not a grade this qualification offers ─────────────────────────────

    [Theory]
    [InlineData("9")]        // a GCSE grade posted against a BTEC
    [InlineData("A*")]
    [InlineData("D3")]       // plausible but not in this scale
    [InlineData("<script>")]
    public void A_grade_the_qualification_does_not_offer_is_treated_as_unanswered(string chosen)
    {
        // Fail closed: a forged post must not smuggle a value the picker never offered into an
        // enquiry the DfE will act on.
        Assert.Equal(Required, Validate(chosen, Btec, "M1"));
    }

    [Fact]
    public void A_grade_from_a_different_qualifications_scale_is_rejected()
        => Assert.Equal(Required, Validate("24D", Btec, "M1"));

    // ── 4. The qualification is missing from the reference data ──────────────

    [Fact]
    public void With_no_reference_data_nothing_can_be_submitted()
    {
        // A reference-data gap is not the user's fault, but letting the enquiry through would send
        // the DfE a grade nobody could confirm is valid. The page explains the gap; validation holds.
        Assert.Equal(Required, Validate(null, null, "M1"));
        Assert.Equal(Required, Validate("D1", null, "M1"));
    }

    [Fact]
    public void With_an_empty_grade_scale_nothing_can_be_submitted()
    {
        var empty = new GradeReference
        {
            Qan = "00000000", QualificationTitle = "Unknown", PassGrades = [], FailGrades = []
        };

        Assert.Equal(Required, Validate("D1", empty, "M1"));
    }

    [Fact]
    public void Must_differ_is_reported_before_the_grade_is_checked_against_the_scale()
    {
        // Rule order matters: a user who picks the grade the result already holds gets the message
        // that explains their mistake, not a bare "select a grade". The only way to reach this with
        // no reference data is a forged post — the picker would be empty — so either message would
        // do there; the ordering is chosen for the case a real user can hit.
        Assert.Equal(MustDiffer, Validate("M1", null, "M1"));
        Assert.Equal(MustDiffer, Validate("M1", Btec, "M1"));
    }

    // ── Current grade edge cases ─────────────────────────────────────────────

    [Fact]
    public void A_valid_grade_passes_when_the_current_grade_is_unknown()
    {
        // Nothing to compare against, so the must-differ rule cannot fire — but the grade must still
        // be one the qualification offers.
        Assert.Null(Validate("D1", Btec, null));
        Assert.Equal(Required, Validate("9", Btec, null));
    }

    [Fact]
    public void A_valid_grade_passes_when_the_current_grade_is_blank()
        => Assert.Null(Validate("D1", Btec, "   "));

    // ── The generic path is untouched ────────────────────────────────────────

    [Fact]
    public void The_generic_answer_validator_still_handles_other_question_types()
    {
        // Guards against the grade rules leaking into every question.
        var freeText = new Question
        {
            Id = "q-cohort-count", Type = QuestionType.FreeText,
            Title = "How many students?", ValidationFailure = "Enter how many students"
        };

        Assert.Equal("Enter how many students",
            _sut.ValidateAnswer(freeText, new QuestionAnswer { TextValue = "" }, "How many students?", "Enter how many students"));
        Assert.Null(
            _sut.ValidateAnswer(freeText, new QuestionAnswer { TextValue = "10" }, "How many students?", "Enter how many students"));
    }
}
