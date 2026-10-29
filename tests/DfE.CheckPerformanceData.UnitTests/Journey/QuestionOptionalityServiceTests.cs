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

    private static JourneyConditionRef Fact(string name) => JourneyConditionRef.Fact(name, true);

    private static JourneyPage Page(params Question[] questions) =>
        new() { Id = "evidence", Questions = questions.ToList() };

    private static Question Q(string id, IReadOnlyList<JourneyConditionRef>? optionalWhen = null) =>
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
        var ids = sut.GetConditionallyOptionalQuestionIds(Page(Q("evidence", [Fact("A"), Fact("B")]), Q("other")), Ctx());
        Assert.Equal(["evidence"], ids.ToList());
    }

    [Fact]
    public void AnyConditionFalse_QuestionStaysMandatory()
    {
        var sut = new QuestionOptionalityService([Condition("A", true), Condition("B", false)]);
        Assert.Empty(sut.GetConditionallyOptionalQuestionIds(Page(Q("evidence", [Fact("A"), Fact("B")])), Ctx()));
    }

    // ── IsRequireAtLeastOneActive ──────────────────────────────────────────

    private static JourneyPage EvidencePage(bool requireAtLeastOne, IReadOnlyList<JourneyConditionRef>? gate = null) =>
        new()
        {
            Id = "evidence",
            RequireAtLeastOne = requireAtLeastOne,
            RequireAtLeastOneWhen = gate,
            Questions = [Q("evidence")]
        };

    [Fact]
    public void RequireAtLeastOne_Unset_IsNeverActive()
    {
        var sut = new QuestionOptionalityService([Condition("A", true)]);
        Assert.False(sut.IsRequireAtLeastOneActive(EvidencePage(false, [Fact("A")]), Ctx()));
    }

    [Fact]
    public void RequireAtLeastOne_WithNoGate_IsAlwaysActive()
    {
        var sut = new QuestionOptionalityService([Condition("A", false)]);
        Assert.True(sut.IsRequireAtLeastOneActive(EvidencePage(true), Ctx()));
    }

    [Fact]
    public void RequireAtLeastOne_GateTrue_IsActive()
    {
        var sut = new QuestionOptionalityService([Condition("A", true), Condition("B", true)]);
        Assert.True(sut.IsRequireAtLeastOneActive(EvidencePage(true, [Fact("A"), Fact("B")]), Ctx()));
    }

    [Fact]
    public void RequireAtLeastOne_GateFalse_IsNotActive()
    {
        var sut = new QuestionOptionalityService([Condition("A", true), Condition("B", false)]);
        Assert.False(sut.IsRequireAtLeastOneActive(EvidencePage(true, [Fact("A"), Fact("B")]), Ctx()));
    }

    [Fact]
    public void RequireAtLeastOne_UnregisteredGateName_FailsClosedToActive()
    {
        var sut = new QuestionOptionalityService([Condition("A", true)]);
        Assert.True(sut.IsRequireAtLeastOneActive(EvidencePage(true, [Fact("Missing")]), Ctx()));
    }

    [Fact]
    public void UnregisteredConditionName_FailsClosedToMandatory()
    {
        var sut = new QuestionOptionalityService([Condition("A", true)]);
        Assert.Empty(sut.GetConditionallyOptionalQuestionIds(Page(Q("evidence", [Fact("A"), Fact("Missing")])), Ctx()));
    }

    // ── Answer tests ─────────────────────────────────────────────────────────

    private static JourneyConditionContext AnswerCtx(params (string Id, string? Value)[] answers)
    {
        var journey = new RequestState();
        foreach (var (id, value) in answers)
            journey.QuestionAnswers[id] = new QuestionAnswer { TextValue = value };
        return new() { Journey = journey, User = new JourneyUserContext() };
    }

    private static readonly IReadOnlyList<JourneyConditionRef> TwoAnswerTests =
    [
        JourneyConditionRef.AnswerIn("reason", "english-not-first-language"),
        JourneyConditionRef.AnswerIn("first-language", "other", "not-known")
    ];

    [Theory]
    [InlineData("english-not-first-language", "other", true)]
    [InlineData("english-not-first-language", "not-known", true)]
    [InlineData("english-not-first-language", "english", false)]
    [InlineData("pupil-died", "other", false)]
    [InlineData("english-not-first-language", null, false)]
    [InlineData(null, "other", false)]
    public void OptionalWhen_AnswerTests_AllMustHold(string? reason, string? language, bool optional)
    {
        var sut = new QuestionOptionalityService([]);
        var ctx = AnswerCtx(("reason", reason), ("first-language", language));

        var ids = sut.GetConditionallyOptionalQuestionIds(Page(Q("evidence", TwoAnswerTests)), ctx);

        Assert.Equal(optional, ids.Contains("evidence"));
    }

    [Fact]
    public void OptionalWhen_AnswerTest_WithNoAnswer_StaysMandatory()
    {
        var sut = new QuestionOptionalityService([]);

        Assert.Empty(sut.GetConditionallyOptionalQuestionIds(Page(Q("evidence", TwoAnswerTests)), Ctx()));
    }

    [Theory]
    [InlineData("other", true)]
    [InlineData("apprentice", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void RequireAtLeastOneWhen_AnswerTest_FailsClosedToActive(string? reason, bool active)
    {
        var sut = new QuestionOptionalityService([]);
        var page = EvidencePage(true, [JourneyConditionRef.AnswerIn("not-on-roll-reason", "other")]);

        Assert.Equal(active, sut.IsRequireAtLeastOneActive(page, AnswerCtx(("not-on-roll-reason", reason))));
    }

    [Fact]
    public void RequireAtLeastOneWhen_AnswerTest_WithNoAnswer_IsActive()
    {
        var sut = new QuestionOptionalityService([]);
        var page = EvidencePage(true, [JourneyConditionRef.AnswerIn("not-on-roll-reason", "other")]);

        Assert.True(sut.IsRequireAtLeastOneActive(page, Ctx()));
    }
}
