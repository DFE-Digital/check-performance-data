using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Web.Controllers.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class PageViewModelTests
{
    private static PageViewModel Build(JourneyPage page, string pupilName = "Jane Doe") => new()
    {
        Page = page,
        Answers = [],
        QuestionModels = [],
        PupilName = pupilName
    };

    [Fact]
    public void PageTitle_uses_explicit_PageTitle_verbatim_when_set()
    {
        var vm = Build(new JourneyPage
        {
            Id = "p1",
            PageTitle = "Reason for removal",
            Title = "Why are you asking to remove {pupilName}?"
        });

        Assert.Equal("Reason for removal", vm.PageTitle);
    }

    [Fact]
    public void PageTitle_never_contains_the_pupil_name_even_when_PageTitle_unset()
    {
        var vm = Build(new JourneyPage
        {
            Id = "p1",
            Title = "Why are you asking to remove {pupilName} from the performance data?"
        });

        Assert.Equal("Why are you asking to remove the pupil from the performance data?", vm.PageTitle);
        Assert.DoesNotContain("Jane Doe", vm.PageTitle);
    }

    [Fact]
    public void PageTitle_falls_back_to_stripped_single_question_title()
    {
        var vm = Build(new JourneyPage
        {
            Id = "p1",
            Questions = [new Question { Id = "q1", Type = QuestionType.FreeText, Title = "What is {pupilName}'s first language?" }]
        });

        Assert.Equal("What is the pupil's first language?", vm.PageTitle);
    }

    [Fact]
    public void ResolvedTitle_still_contains_the_pupil_name_for_the_visible_heading()
    {
        var vm = Build(new JourneyPage
        {
            Id = "p1",
            Title = "Why are you asking to remove {pupilName} from the performance data?"
        });

        Assert.Equal("Why are you asking to remove Jane Doe from the performance data?", vm.ResolvedTitle);
    }
}
