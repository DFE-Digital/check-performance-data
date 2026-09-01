using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB: a multi-select question whose answer is a list of option values. "At least one must be
// selected" is the ordinary required rule, not a new concept — an optional checkbox list is
// still allowed to be left empty.
public sealed class CheckboxQuestionTests
{
    private readonly JourneyValidationService _sut = new();

    private static Question MakeQuestion(bool optional = false, string? validationFailure = null) => new()
    {
        Id = "years-to-remove",
        Type = QuestionType.Checkbox,
        Title = "Which years do you want to remove {pupilName} from?",
        Optional = optional,
        ValidationFailure = validationFailure,
        Options =
        [
            new QuestionOption { Value = "2025-2026", Label = "2025 to 2026" },
            new QuestionOption { Value = "2024-2025", Label = "2024 to 2025" },
            new QuestionOption { Value = "2023-2024", Label = "2023 to 2024" }
        ]
    };

    // ── enum contract ───────────────────────────────────────────────────────

    [Fact]
    public void Checkbox_is_appended_last_so_existing_flow_values_are_unmoved()
    {
        Assert.Equal(8, (int)QuestionType.Checkbox);
    }

    // ── IsAnswered ──────────────────────────────────────────────────────────

    [Fact]
    public void IsAnswered_is_false_when_nothing_is_ticked()
    {
        Assert.False(_sut.IsAnswered(MakeQuestion(), new QuestionAnswer { SelectedValues = [] }));
    }

    [Fact]
    public void IsAnswered_is_false_when_the_answer_is_missing()
    {
        Assert.False(_sut.IsAnswered(MakeQuestion(), null));
    }

    [Fact]
    public void IsAnswered_is_true_when_one_box_is_ticked()
    {
        var answer = new QuestionAnswer { SelectedValues = ["2024-2025"] };

        Assert.True(_sut.IsAnswered(MakeQuestion(), answer));
    }

    [Fact]
    public void IsAnswered_ignores_TextValue_so_a_stray_post_cannot_pass_for_a_selection()
    {
        var answer = new QuestionAnswer { TextValue = "2024-2025" };

        Assert.False(_sut.IsAnswered(MakeQuestion(), answer));
    }

    // ── ValidateAnswer ──────────────────────────────────────────────────────

    [Fact]
    public void ValidateAnswer_returns_the_questions_own_message_when_nothing_is_ticked()
    {
        var error = _sut.ValidateAnswer(
            MakeQuestion(), new QuestionAnswer { SelectedValues = [] },
            "Which years do you want to remove Billy B from?",
            "Select which years you want to remove Billy B from");

        Assert.Equal("Select which years you want to remove Billy B from", error);
    }

    [Fact]
    public void ValidateAnswer_falls_back_to_the_generic_message_when_the_config_gives_none()
    {
        var error = _sut.ValidateAnswer(
            MakeQuestion(), new QuestionAnswer { SelectedValues = [] }, "My question");

        Assert.Equal("My question is required", error);
    }

    [Fact]
    public void ValidateAnswer_accepts_one_ticked_box()
    {
        var answer = new QuestionAnswer { SelectedValues = ["2023-2024"] };

        Assert.Null(_sut.ValidateAnswer(MakeQuestion(), answer, "My question"));
    }

    [Fact]
    public void ValidateAnswer_accepts_several_ticked_boxes()
    {
        var answer = new QuestionAnswer { SelectedValues = ["2025-2026", "2023-2024"] };

        Assert.Null(_sut.ValidateAnswer(MakeQuestion(), answer, "My question"));
    }

    // ── display ─────────────────────────────────────────────────────────────

    [Fact]
    public void Display_joins_the_labels_in_config_order_not_post_order()
    {
        var answer = new QuestionAnswer { SelectedValues = ["2023-2024", "2025-2026"] };

        Assert.Equal("2025 to 2026, 2023 to 2024", CheckboxAnswerDisplay.Join(MakeQuestion(), answer));
    }

    [Fact]
    public void Display_falls_back_to_the_raw_value_when_an_option_has_been_retired_from_the_config()
    {
        var answer = new QuestionAnswer { SelectedValues = ["2025-2026", "2019-2020"] };

        Assert.Equal("2025 to 2026, 2019-2020", CheckboxAnswerDisplay.Join(MakeQuestion(), answer));
    }

    [Fact]
    public void Display_is_empty_when_nothing_is_ticked()
    {
        Assert.Equal(string.Empty, CheckboxAnswerDisplay.Join(MakeQuestion(), new QuestionAnswer()));
    }

    [Fact]
    public void Display_raw_joins_the_values_for_the_request_document()
    {
        var answer = new QuestionAnswer { SelectedValues = ["2023-2024", "2025-2026"] };

        Assert.Equal("2025-2026,2023-2024", CheckboxAnswerDisplay.JoinValues(MakeQuestion(), answer));
    }
}
