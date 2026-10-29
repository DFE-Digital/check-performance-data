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

    private static JourneyConditionRef Fact(string name) => JourneyConditionRef.Fact(name, true);

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
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A", VisibleWhen = [Fact("Flag")] });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Single(result);
        Assert.Equal("a", result[0].Value);
    }

    [Fact]
    public void OptionWithConditionFalse_IsHidden()
    {
        var sut = new OptionVisibilityService([new FakeCondition("Flag", false)]);
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A", VisibleWhen = [Fact("Flag")] });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Empty(result);
    }

    [Fact]
    public void OptionWithUnknownCondition_IsHidden()
    {
        var sut = new OptionVisibilityService([new FakeCondition("Other", true)]);
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A", VisibleWhen = [Fact("Missing")] });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Empty(result);
    }

    [Fact]
    public void MixedOptions_PreserveInputOrderOfVisibleOnes()
    {
        var sut = new OptionVisibilityService([new FakeCondition("Flag", false)]);
        var question = RadioWith(
            new QuestionOption { Value = "first", Label = "First" },
            new QuestionOption { Value = "hidden", Label = "Hidden", VisibleWhen = [Fact("Flag")] },
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
            new QuestionOption { Value = "b", Label = "B", VisibleWhen = [Fact("Flag")] });

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
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A", VisibleWhen = [Fact("A"), Fact("B")] });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Single(result);
    }

    [Fact]
    public void MultipleConditions_OneFalse_IsHidden()
    {
        var sut = new OptionVisibilityService([new FakeCondition("A", true), new FakeCondition("B", false)]);
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A", VisibleWhen = [Fact("A"), Fact("B")] });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Empty(result);
    }

    [Fact]
    public void MultipleConditions_OneUnregistered_IsHidden()
    {
        var sut = new OptionVisibilityService([new FakeCondition("A", true)]);
        var question = RadioWith(new QuestionOption { Value = "a", Label = "A", VisibleWhen = [Fact("A"), Fact("Missing")] });

        var result = sut.GetVisibleOptions(question, Ctx);

        Assert.Empty(result);
    }

    // ── Question-level visibility (issue #577) ───────────────────────────────

    private static Question QuestionWith(params string[] visibleWhen) =>
        new() { Id = "q", Type = QuestionType.FreeText, Title = "Q", VisibleWhen = [.. visibleWhen.Select(Fact)] };

    [Fact]
    public void QuestionWithoutVisibleWhen_IsVisible()
    {
        var sut = new OptionVisibilityService([]);

        Assert.True(sut.IsQuestionVisible(QuestionWith(), Ctx));
    }

    [Fact]
    public void QuestionWithConditionTrue_IsVisible()
    {
        var sut = new OptionVisibilityService([new FakeCondition("Flag", true)]);

        Assert.True(sut.IsQuestionVisible(QuestionWith("Flag"), Ctx));
    }

    [Fact]
    public void QuestionWithConditionFalse_IsHidden()
    {
        var sut = new OptionVisibilityService([new FakeCondition("Flag", false)]);

        Assert.False(sut.IsQuestionVisible(QuestionWith("Flag"), Ctx));
    }

    [Fact]
    public void QuestionWithUnknownCondition_IsHidden()
    {
        var sut = new OptionVisibilityService([new FakeCondition("Other", true)]);

        Assert.False(sut.IsQuestionVisible(QuestionWith("Missing"), Ctx));
    }

    [Fact]
    public void QuestionMultipleConditions_AllTrue_IsVisible()
    {
        var sut = new OptionVisibilityService([new FakeCondition("A", true), new FakeCondition("B", true)]);

        Assert.True(sut.IsQuestionVisible(QuestionWith("A", "B"), Ctx));
    }

    [Fact]
    public void QuestionMultipleConditions_OneFalse_IsHidden()
    {
        var sut = new OptionVisibilityService([new FakeCondition("A", true), new FakeCondition("B", false)]);

        Assert.False(sut.IsQuestionVisible(QuestionWith("A", "B"), Ctx));
    }

    // ── Answer tests ─────────────────────────────────────────────────────────

    private static JourneyConditionContext AnswerCtx(string questionId, string? value) => new()
    {
        Journey = new RequestState
        {
            QuestionAnswers = { [questionId] = new QuestionAnswer { TextValue = value } }
        },
        User = new JourneyUserContext()
    };

    private static QuestionOption AnswerGated() => new()
    {
        Value = "a", Label = "A",
        VisibleWhen = [JourneyConditionRef.AnswerIn("reason", "other", "unknown")]
    };

    [Theory]
    [InlineData("other", true)]
    [InlineData("unknown", true)]
    [InlineData("english", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Option_AnswerTest_ShowsOnlyForAListedValue(string? answer, bool shown)
    {
        var sut = new OptionVisibilityService([]);

        var result = sut.GetVisibleOptions(RadioWith(AnswerGated()), AnswerCtx("reason", answer));

        Assert.Equal(shown, result.Count == 1);
    }

    [Fact]
    public void Option_AnswerTest_WithNoAnswer_IsHidden()
    {
        var sut = new OptionVisibilityService([]);

        Assert.Empty(sut.GetVisibleOptions(RadioWith(AnswerGated()), Ctx));
    }

    [Fact]
    public void Question_AnswerTest_AndName_MustBothHold()
    {
        var sut = new OptionVisibilityService([new FakeCondition("Flag", true)]);
        var question = new Question
        {
            Id = "q", Type = QuestionType.FreeText, Title = "Q",
            VisibleWhen = [Fact("Flag"), JourneyConditionRef.AnswerIn("reason", "other")]
        };

        Assert.True(sut.IsQuestionVisible(question, AnswerCtx("reason", "other")));
        Assert.False(sut.IsQuestionVisible(question, AnswerCtx("reason", "english")));
        Assert.False(sut.IsQuestionVisible(question, Ctx));
    }

    // ── Fact tests and anyOf ─────────────────────────────────────────────────

    private static QuestionOption Gated(params JourneyConditionRef[] visibleWhen) =>
        new() { Value = "a", Label = "A", VisibleWhen = visibleWhen };

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Option_FactTest_ShowsWhenTheFactGivesTheExpectedValue(bool fact, bool @is, bool shown)
    {
        var sut = new OptionVisibilityService([new FakeCondition("school.flag", fact)]);

        var result = sut.GetVisibleOptions(RadioWith(Gated(JourneyConditionRef.Fact("school.flag", @is))), Ctx);

        Assert.Equal(shown, result.Count == 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Option_FactTest_WithAnUnknownFact_IsHidden(bool @is)
    {
        var sut = new OptionVisibilityService([]);

        Assert.Empty(sut.GetVisibleOptions(RadioWith(Gated(JourneyConditionRef.Fact("unknown", @is))), Ctx));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void Option_AnyOf_ShowsWhenOneEntryHolds(bool a, bool b, bool shown)
    {
        var sut = new OptionVisibilityService([new FakeCondition("A", a), new FakeCondition("B", b)]);
        var option = Gated(JourneyConditionRef.AnyOfThese(JourneyConditionRef.Fact("A", true), JourneyConditionRef.Fact("B", true)));

        Assert.Equal(shown, sut.GetVisibleOptions(RadioWith(option), Ctx).Count == 1);
    }

    [Fact]
    public void Option_AnyOf_WithATrueEntry_IsShown_EvenWhenAnotherIsUndecided()
    {
        var sut = new OptionVisibilityService([new FakeCondition("A", true)]);
        var option = Gated(JourneyConditionRef.AnyOfThese(JourneyConditionRef.Fact("A", true), JourneyConditionRef.Fact("unknown", true)));

        Assert.Single(sut.GetVisibleOptions(RadioWith(option), Ctx));
    }

    [Fact]
    public void Option_AnyOf_WithNoTrueEntryAndAnUndecidedOne_IsHidden()
    {
        var sut = new OptionVisibilityService([new FakeCondition("A", false)]);
        var option = Gated(JourneyConditionRef.AnyOfThese(JourneyConditionRef.Fact("A", true), JourneyConditionRef.Fact("unknown", true)));

        Assert.Empty(sut.GetVisibleOptions(RadioWith(option), Ctx));
    }

    [Fact]
    public void Option_AnyOf_IsAndedWithTheOtherEntries()
    {
        var sut = new OptionVisibilityService([new FakeCondition("A", true), new FakeCondition("C", false)]);
        var option = Gated(
            JourneyConditionRef.Fact("C", true),
            JourneyConditionRef.AnyOfThese(JourneyConditionRef.Fact("A", true), JourneyConditionRef.Fact("B", true)));

        Assert.Empty(sut.GetVisibleOptions(RadioWith(option), Ctx));
    }
}
