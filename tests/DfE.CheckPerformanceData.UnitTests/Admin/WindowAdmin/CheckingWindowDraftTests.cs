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
    public void The_exercise_step_comes_after_the_window_type_because_the_type_pre_ticks_it()
    {
        CheckingWindowDraft draft = new() { Title = "A window" };

        Assert.Equal("WindowType", draft.NextController(_url));
    }

    [Fact]
    public void The_exercise_step_follows_the_key_stage()
    {
        CheckingWindowDraft draft = new()
        {
            Title = "A window",
            CheckingWindowType = CheckingWindowType.Post16,
            KeyStage = KeyStages.Post16
        };

        Assert.Equal("Exercises", draft.NextController(_url));
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

    private static CheckingWindowDraft Complete() => new()
    {
        Title = "16 to 19 2027",
        CheckingWindowType = CheckingWindowType.Post16,
        KeyStage = KeyStages.Post16,
        Exercises =
        [
            new ExerciseDraft
            {
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = new DateTime(2027, 1, 1),
                EndDate = new DateTime(2027, 1, 14, 17, 0, 0),
                SortOrder = 0
            },
            new ExerciseDraft
            {
                ExerciseType = CheckingExerciseType.ResultsEnquiry,
                StartDate = new DateTime(2027, 1, 1),
                EndDate = new DateTime(2027, 6, 30, 17, 0, 0),
                SortOrder = 1
            }
        ]
    };
}
