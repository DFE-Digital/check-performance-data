using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #319: the wizard's step order and the derived outer date pair. There is no window-level date step
// any more, so a draft is only ever complete once every ticked exercise has its own dates.
public class CheckingWindowDraftTests
{
    private readonly IUrlHelper _url = Substitute.For<IUrlHelper>();

    public CheckingWindowDraftTests()
    {
        // Echo the controller name back, so a test can assert which step comes next.
        _url.Action(Arg.Any<UrlActionContext>())
            .Returns(call => call.Arg<UrlActionContext>().Controller);
    }

    [Fact]
    public void The_window_type_step_follows_the_title()
    {
        CheckingWindowDraft draft = new() { Title = "A window" };

        Assert.Equal("WindowType", draft.NextController(_url));
    }

    [Fact]
    public void A_draft_with_a_type_but_no_exercises_goes_back_to_the_window_type_step()
    {
        // The window type step gives the draft its exercises. The wizard has no exercise step, so
        // the window type step is the only place that can fill them in.
        CheckingWindowDraft draft = new()
        {
            Title = "A window",
            CheckingWindowType = CheckingWindowType.Post16
        };

        Assert.Equal("WindowType", draft.NextController(_url));
    }

    [Theory]
    [InlineData(CheckingWindowType.Post16, new[] { CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry })]
    [InlineData(CheckingWindowType.KS4June, new[] { CheckingExerciseType.PupilData })]
    public void The_window_type_gives_the_draft_its_default_exercises(
        CheckingWindowType type, CheckingExerciseType[] expected)
    {
        CheckingWindowDraft draft = new() { Title = "A window", CheckingWindowType = type };

        draft.UseDefaultExercises();

        Assert.Equal(expected, draft.Exercises.Select(e => e.ExerciseType));
        Assert.Equal(expected.Select(WindowExercises.DefaultTabOrder), draft.Exercises.Select(e => e.TabOrder));
        Assert.Equal("ExerciseDates", draft.NextController(_url));
    }

    [Fact]
    public void Changing_the_window_type_keeps_the_dates_of_an_exercise_the_new_type_also_runs()
    {
        CheckingWindowDraft draft = Complete();
        DateTime pupilDataEnd = draft.Exercises[0].EndDate!.Value;
        draft.CheckingWindowType = CheckingWindowType.KS4June;

        draft.UseDefaultExercises();

        ExerciseDraft only = Assert.Single(draft.Exercises);
        Assert.Equal(CheckingExerciseType.PupilData, only.ExerciseType);
        Assert.Equal(pupilDataEnd, only.EndDate);
    }

    [Fact]
    public void The_key_stage_is_derived_from_the_window_type()
    {
        Assert.Null(new CheckingWindowDraft().KeyStage);
        Assert.Equal(KeyStages.KS4,
            new CheckingWindowDraft { CheckingWindowType = CheckingWindowType.KS4Autumn }.KeyStage);
    }

    [Fact]
    public void Each_ticked_exercise_is_asked_for_its_dates_in_turn()
    {
        CheckingWindowDraft draft = Complete();
        draft.Exercises[1].StartDate = null;
        draft.Exercises[1].EndDate = null;

        Assert.Equal("ExerciseDates", draft.NextController(_url));
        Assert.Equal(CheckingExerciseType.ResultsEnquiry, draft.FirstUndatedExercise!.ExerciseType);
    }

    [Fact]
    public void A_fully_dated_draft_goes_to_the_check_answers_step()
    {
        Assert.Equal("CreateCheckingWindow", Complete().NextController(_url));
    }

    [Fact]
    public void The_outer_dates_are_the_union_of_the_exercises()
    {
        CheckingWindowDraft draft = Complete();

        Assert.Equal(new DateTime(2027, 1, 1), draft.StartDate);
        Assert.Equal(new DateTime(2027, 6, 30, 17, 0, 0), draft.EndDate);
    }

    [Fact]
    public void The_outer_dates_are_null_while_any_exercise_is_undated()
    {
        CheckingWindowDraft draft = Complete();
        draft.Exercises[1].EndDate = null;

        Assert.Null(draft.EndDate);
    }

    [Fact]
    public void A_draft_is_not_valid_while_an_exercise_is_undated()
    {
        CheckingWindowDraft draft = Complete();
        draft.Exercises[1].StartDate = null;

        Assert.False(draft.IsValid);
    }

    [Fact]
    public void A_draft_is_not_valid_when_an_exercise_ends_before_it_starts()
    {
        CheckingWindowDraft draft = Complete();
        draft.Exercises[0].EndDate = draft.Exercises[0].StartDate!.Value.AddDays(-1);

        Assert.False(draft.IsValid);
    }

    [Fact]
    public void A_draft_is_not_valid_when_an_exercise_starts_in_the_past()
    {
        CheckingWindowDraft draft = Complete();
        draft.Exercises[0].StartDate = new DateTime(2020, 1, 1);

        Assert.False(draft.IsValid);
    }

    [Fact]
    public void A_complete_draft_is_valid()
    {
        Assert.True(Complete().IsValid);
    }

    [Fact]
    public void ToExerciseDtos_carries_every_exercise_with_its_own_dates()
    {
        var dtos = Complete().ToExerciseDtos();

        Assert.Equal(2, dtos.Count);
        Assert.Equal(new DateTime(2027, 1, 14, 17, 0, 0),
            dtos.Single(d => d.ExerciseType == CheckingExerciseType.PupilData).EndDate);
        Assert.Equal(new DateTime(2027, 6, 30, 17, 0, 0),
            dtos.Single(d => d.ExerciseType == CheckingExerciseType.ResultsEnquiry).EndDate);
    }

    [Fact]
    public void Wizard_exercises_get_a_default_tab_name_and_start_disabled()
    {
        // Disabled until an admin has loaded data and enables it, so schools see nothing before then.
        List<CheckingExerciseDto> dtos = Complete().ToExerciseDtos();

        Assert.Equal("Students", dtos.Single(d => d.ExerciseType == CheckingExerciseType.PupilData).TabName);
        Assert.Equal("Results", dtos.Single(d => d.ExerciseType == CheckingExerciseType.ResultsEnquiry).TabName);
        Assert.All(dtos, d => Assert.False(d.IsEnabled));
    }

    [Fact]
    public void A_wizard_results_enquiry_starts_with_the_late_results_warning_on()
    {
        List<CheckingExerciseDto> dtos = Complete().ToExerciseDtos();

        Assert.True(dtos.Single(d => d.ExerciseType == CheckingExerciseType.ResultsEnquiry).ShowLateResultsWarning);
        Assert.False(dtos.Single(d => d.ExerciseType == CheckingExerciseType.PupilData).ShowLateResultsWarning);
    }

    [Fact]
    public void A_wizard_KS4_June_pupil_data_exercise_starts_with_included_and_non_included_tabs()
    {
        CheckingWindowDraft draft = Complete();
        draft.CheckingWindowType = CheckingWindowType.KS4June;

        List<CheckingExerciseDto> dtos = draft.ToExerciseDtos();

        Assert.Equal(ExerciseLayout.InclusionTabs, dtos.Single(d => d.ExerciseType == CheckingExerciseType.PupilData).Layout);
        Assert.Equal(ExerciseLayout.Table, dtos.Single(d => d.ExerciseType == CheckingExerciseType.ResultsEnquiry).Layout);
    }

    [Fact]
    public void A_wizard_16_to_19_pupil_data_exercise_starts_as_a_table()
    {
        List<CheckingExerciseDto> dtos = Complete().ToExerciseDtos();

        Assert.Equal(ExerciseLayout.Table, dtos.Single(d => d.ExerciseType == CheckingExerciseType.PupilData).Layout);
    }

    private static CheckingWindowDraft Complete() => new()
    {
        Title = "16 to 19 2027",
        CheckingWindowType = CheckingWindowType.Post16,
        Exercises =
        [
            new ExerciseDraft
            {
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = new DateTime(2027, 1, 1),
                EndDate = new DateTime(2027, 1, 14, 17, 0, 0),
                TabOrder = 0
            },
            new ExerciseDraft
            {
                ExerciseType = CheckingExerciseType.ResultsEnquiry,
                StartDate = new DateTime(2027, 1, 1),
                EndDate = new DateTime(2027, 6, 30, 17, 0, 0),
                TabOrder = 1
            }
        ]
    };
}
