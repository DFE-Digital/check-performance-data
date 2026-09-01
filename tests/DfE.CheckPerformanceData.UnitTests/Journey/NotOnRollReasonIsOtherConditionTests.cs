using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public sealed class NotOnRollReasonIsOtherConditionTests
{
    private readonly NotOnRollReasonIsOtherCondition _sut = new();

    private static JourneyConditionContext Ctx(string? reason)
    {
        var journey = new RequestState();
        if (reason is not null)
            journey.QuestionAnswers["not-on-roll-reason"] = new QuestionAnswer { TextValue = reason };
        return new JourneyConditionContext { Journey = journey, User = new JourneyUserContext() };
    }

    [Fact]
    public void Name_IsTheNameTheConfigReferences() =>
        Assert.Equal("NotOnRollReasonIsOther", _sut.Name);

    [Fact]
    public void Other_IsTrue() => Assert.True(_sut.Evaluate(Ctx("other")));

    [Theory]
    [InlineData("apprentice")]
    [InlineData("external-candidate")]
    [InlineData("international-student")]
    public void EverySelfExplanatoryReason_IsFalse(string reason) =>
        Assert.False(_sut.Evaluate(Ctx(reason)));

    [Fact]
    public void UnansweredReason_FailsClosed() => Assert.True(_sut.Evaluate(Ctx(null)));

    [Fact]
    public void BlankReason_FailsClosed() => Assert.True(_sut.Evaluate(Ctx("")));
}
