using DfE.CheckPerformanceData.Application.Journey;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public sealed class QuestionOptionalityServiceTests
{
    private static IJourneyCondition Condition(string name, bool result)
    {
        var c = Substitute.For<IJourneyCondition>();
        c.Name.Returns(name);
        c.Evaluate(Arg.Any<JourneyConditionContext>()).Returns(result);
        return c;
    }

    private static JourneyConditionContext Ctx() =>
        new() { Journey = new RequestState(), User = new JourneyUserContext() };

    private static JourneyPage Page(params Question[] questions) =>
        new() { Id = "evidence", Questions = questions.ToList() };

    private static Question Q(string id, IReadOnlyList<string>? optionalWhen = null) =>
        new() { Id = id, Type = QuestionType.FileUpload, Title = "t", OptionalWhen = optionalWhen };

    [Fact]
    public void NoOptionalWhen_ReturnsEmpty()
    {
        var sut = new QuestionOptionalityService([Condition("A", true)]);
        Assert.Empty(sut.GetConditionallyOptionalQuestionIds(Page(Q("evidence")), Ctx()));
    }

    [Fact]
    public void AllConditionsTrue_QuestionIsOptional()
    {
        var sut = new QuestionOptionalityService([Condition("A", true), Condition("B", true)]);
        var ids = sut.GetConditionallyOptionalQuestionIds(Page(Q("evidence", ["A", "B"]), Q("other")), Ctx());
        Assert.Equal(["evidence"], ids.ToList());
    }

    [Fact]
    public void AnyConditionFalse_QuestionStaysMandatory()
    {
        var sut = new QuestionOptionalityService([Condition("A", true), Condition("B", false)]);
        Assert.Empty(sut.GetConditionallyOptionalQuestionIds(Page(Q("evidence", ["A", "B"])), Ctx()));
    }

    [Fact]
    public void UnregisteredConditionName_FailsClosedToMandatory()
    {
        var sut = new QuestionOptionalityService([Condition("A", true)]);
        Assert.Empty(sut.GetConditionallyOptionalQuestionIds(Page(Q("evidence", ["A", "Missing"])), Ctx()));
    }
}
