using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Web.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

// AB#296648: the results-enquiry analytics contract.
//
// Two things are pinned. The field and event NAMES, because they become BigQuery columns that reports
// are written against — a rename is a breaking change downstream, not a refactor. And what is NOT in
// the payload: house law says no grade, QAN, student name or free text ever leaves as a plain field,
// and the reference number is always masked.
public sealed class ResultsEnquiryEventsTests
{
    private static ResultsEnquiryStartedEvent Started(bool guidanceShown = true) => new()
    {
        EnquiryType = "incorrect-grade",
        CheckingWindowType = "Post16",
        LateResultsGuidanceShown = guidanceShown
    };

    private static ResultsEnquirySubmittedEvent Submitted(bool cohortWide = true) => new()
    {
        EnquiryType = "incorrect-grade",
        CohortWide = cohortWide,
        CheckingWindowType = "Post16",
        ReferenceNumber = "CYPMD_16to19_RE_4F9C2A1"
    };

    // ── Event names ──────────────────────────────────────────────────────────

    [Fact]
    public void The_event_names_are_snake_case_and_plural_to_match_the_window_model()
    {
        // "ResultsEnquiry" (plural) throughout, per docs/16-19-window-model.md.
        Assert.Equal("results_enquiry_started", Started().EventType);
        Assert.Equal("results_enquiry_submitted", Submitted().EventType);
    }

    // ── Started ──────────────────────────────────────────────────────────────

    [Fact]
    public void The_started_event_carries_its_three_fields()
    {
        var fields = Started().Fields.ToDictionary(f => f.Name, f => f.Value);

        Assert.Equal("incorrect-grade", fields["enquiry_type"]);
        Assert.Equal("Post16", fields["checking_window_type"]);
        Assert.Equal("true", fields["late_results_guidance_shown"]);
    }

    [Fact]
    public void The_guidance_flag_is_lowercase_so_it_reads_as_a_boolean_downstream()
    {
        // .ToString() on a bool gives "True"; BigQuery consumers expect "true"/"false".
        Assert.Equal("false", Started(guidanceShown: false).Fields
            .Single(f => f.Name == "late_results_guidance_shown").Value);
    }

    [Fact]
    public void Nothing_in_the_started_event_is_masked_because_nothing_in_it_is_personal()
        => Assert.All(Started().Fields, f => Assert.False(f.Hidden));

    // ── Submitted ────────────────────────────────────────────────────────────

    [Fact]
    public void The_submitted_event_carries_its_four_fields()
    {
        var fields = Submitted().Fields.ToDictionary(f => f.Name, f => f.Value);

        Assert.Equal("incorrect-grade", fields["enquiry_type"]);
        Assert.Equal("true", fields["cohort_wide"]);
        Assert.Equal("Post16", fields["checking_window_type"]);
        Assert.Equal("CYPMD_16to19_RE_4F9C2A1", fields["reference_number"]);
    }

    [Fact]
    public void The_reference_number_is_always_masked()
    {
        // House law, pending the DPIA classification. The hash still links started -> submitted.
        Assert.True(Submitted().Fields.Single(f => f.Name == "reference_number").Hidden);
    }

    [Fact]
    public void Only_the_reference_number_is_masked()
    {
        var hidden = Submitted().Fields.Where(f => f.Hidden).Select(f => f.Name).ToArray();

        Assert.Equal(["reference_number"], hidden);
    }

    [Fact]
    public void The_cohort_flag_reflects_the_branch()
        => Assert.Equal("false", Submitted(cohortWide: false).Fields
            .Single(f => f.Name == "cohort_wide").Value);

    // ── What must never appear ───────────────────────────────────────────────

    [Theory]
    [InlineData("grade")]
    [InlineData("qan")]
    [InlineData("pupil")]
    [InlineData("student")]
    [InlineData("name")]
    [InlineData("comment")]
    [InlineData("additional")]
    [InlineData("session")]
    [InlineData("count")]
    public void No_field_carries_grade_qualification_identity_or_free_text(string forbidden)
    {
        // A grade paired with a school and a date is identifying, and the comments box is free text by
        // definition. cohort_wide is a boolean deliberately, not the count.
        foreach (var evt in new AnalyticsEvent[] { Started(), Submitted() })
            Assert.DoesNotContain(forbidden, string.Join(" ", evt.Fields.Select(f => f.Name)));
    }

    // ── The validation-error taxonomy ────────────────────────────────────────

    [Fact]
    public void An_unanswered_grade_picker_codes_as_required()
    {
        var question = new Question
        {
            Id = "q-revised-grade", Type = QuestionType.GradeSelect, Title = "What should the revised grade be?"
        };

        Assert.Equal("required", ValidationErrorCoding.ForQuestion(question, isAnswered: false));
    }

    [Fact]
    public void An_answered_but_rejected_grade_codes_as_a_bad_selection_not_generic_invalid()
    {
        // A grade picker is a selection control, so it should code alongside Radio and Autocomplete.
        // Without an explicit case it fell through to "invalid", which loses the distinction the
        // taxonomy exists to make.
        var question = new Question
        {
            Id = "q-revised-grade", Type = QuestionType.GradeSelect, Title = "What should the revised grade be?"
        };

        Assert.Equal("selection_invalid", ValidationErrorCoding.ForQuestion(question, isAnswered: true));
    }

    [Fact]
    public void The_existing_question_types_keep_their_codes()
    {
        Question Q(QuestionType type) => new() { Id = "q", Type = type, Title = "t" };

        Assert.Equal("bad_date", ValidationErrorCoding.ForQuestion(Q(QuestionType.Date), true));
        Assert.Equal("too_long", ValidationErrorCoding.ForQuestion(Q(QuestionType.TextArea), true));
        Assert.Equal("selection_invalid", ValidationErrorCoding.ForQuestion(Q(QuestionType.Radio), true));
        Assert.Equal("selection_invalid", ValidationErrorCoding.ForQuestion(Q(QuestionType.Autocomplete), true));
        Assert.Equal("invalid", ValidationErrorCoding.ForQuestion(Q(QuestionType.FreeText), true));
    }
}
