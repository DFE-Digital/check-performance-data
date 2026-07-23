using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class OptionVisibilityServiceTests
{
    private static readonly JourneyConditionContext Ctx = new()
    {
        Journey = new RequestState(),
        User = new JourneyUserContext()
    };

    private sealed class FakeCondition(string name, bool result) : IJourneyCondition
    {
        public string Name => name;
        public bool Evaluate(JourneyConditionContext ctx) => result;
    }

    private static Question RadioWith(params QuestionOption[] options) =>
        new() { Id = "q", Type = QuestionType.Radio, Title = "Q", Options = [.. options] };

    [Fact]
    public void OptionWithoutVisibleWhen_IsShown()
    {
        var sut = new OptionVisibilityService([]);
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A" });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Single(result);
        Assert.Equal("a", result[0].Value);
    }

    [Fact]
    public void OptionWithConditionTrue_IsShown()
    {
        var sut = new OptionVisibilityService([new FakeCondition("Flag", true)]);
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A", VisibleWhen = ["Flag"] });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Single(result);
        Assert.Equal("a", result[0].Value);
    }

    [Fact]
    public void OptionWithConditionFalse_IsHidden()
    {
        var sut = new OptionVisibilityService([new FakeCondition("Flag", false)]);
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A", VisibleWhen = ["Flag"] });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Empty(result);
    }

    [Fact]
    public void OptionWithUnknownCondition_IsHidden()
    {
        var sut = new OptionVisibilityService([new FakeCondition("Other", true)]);
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A", VisibleWhen = ["Missing"] });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Empty(result);
    }

    [Fact]
    public void MixedOptions_PreserveInputOrderOfVisibleOnes()
    {
        var sut = new OptionVisibilityService([new FakeCondition("Flag", false)]);
        var question = RadioWith(
            new QuestionOption { Value = "first", Label = "First" },
            new QuestionOption { Value = "hidden", Label = "Hidden", VisibleWhen = ["Flag"] },
            new QuestionOption { Value = "last", Label = "Last" });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Equal(["first", "last"], result.Select(o => o.Value));
    }

    [Fact]
    public void DoesNotMutateQuestionOptions()
    {
        var sut = new OptionVisibilityService([new FakeCondition("Flag", false)]);
        var question = RadioWith(
            new QuestionOption { Value = "a", Label = "A" },
            new QuestionOption { Value = "b", Label = "B", VisibleWhen = ["Flag"] });

        sut.GetVisibleOptions(question, Ctx);

        Assert.Equal(2, question.Options!.Count);
    }

    [Fact]
    public void QuestionWithNoOptions_ReturnsEmpty()
    {
        var sut = new OptionVisibilityService([]);
        var question = new Question { Id = "q", Type = QuestionType.FreeText, Title = "Q" };

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Empty(result);
    }

    [Fact]
    public void MultipleConditions_AllTrue_IsShown()
    {
        var sut = new OptionVisibilityService([new FakeCondition("A", true), new FakeCondition("B", true)]);
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A", VisibleWhen = ["A", "B"] });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Single(result);
    }

    [Fact]
    public void MultipleConditions_OneFalse_IsHidden()
    {
        var sut = new OptionVisibilityService([new FakeCondition("A", true), new FakeCondition("B", false)]);
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A", VisibleWhen = ["A", "B"] });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Empty(result);
    }

    [Fact]
    public void MultipleConditions_OneUnregistered_IsHidden()
    {
        var sut = new OptionVisibilityService([new FakeCondition("A", true)]);
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A", VisibleWhen = ["A", "Missing"] });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Empty(result);
    }
}
