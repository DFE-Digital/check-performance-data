using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public sealed class EalWouldBeAutoRejectedConditionTests
{
    private readonly EalWouldBeAutoRejectedCondition _sut = new();

    private static JourneyConditionContext Ctx(
        string? reason = "english-not-first-language",
        string? firstLanguage = null,
        List<string>? countryLanguages = null)
    {
        var journey = new RequestState { OriginCountryLanguages = countryLanguages };
        if (reason is not null)
            journey.QuestionAnswers["reason"] = new QuestionAnswer { TextValue = reason };
        if (firstLanguage is not null)
            journey.QuestionAnswers["first-language"] = new QuestionAnswer { TextValue = firstLanguage };
        return new JourneyConditionContext { Journey = journey, User = new JourneyUserContext() };
    }

    [Fact] public void Name_IsStable() => Assert.Equal("EalWouldBeAutoRejected", _sut.Name);

    // AC 001/002: first language English → optional regardless of country
    [Theory]
    [InlineData("english", null)]                       // country languages unknown
    [InlineData("english", "French")]                   // AC 001: France
    [InlineData("english", "English")]                  // AC 002: Nigeria
    public void English_IsTrue(string lang, string? countryLanguage) =>
        Assert.True(_sut.Evaluate(Ctx(firstLanguage: lang,
            countryLanguages: countryLanguage is null ? null : [countryLanguage])));

    // believed-* map to Uncertain in the engine, so they never auto-reject (they go to
    // Scrutiny) — evidence stays mandatory. Scenario 004 was withdrawn (BA decision,
    // 2026-07-28); the waiver mirrors the engine exactly.
    [Theory]
    [InlineData("believed-english", "English")]
    [InlineData("believed-english", null)]
    [InlineData("believed-other", "English")]
    public void BelievedLanguage_IsFalse(string lang, string? countryLanguage) =>
        Assert.False(_sut.Evaluate(Ctx(firstLanguage: lang,
            countryLanguages: countryLanguage is null ? null : [countryLanguage])));

    // AC 003 (Canada): other + English official language
    [Fact]
    public void Other_WithEnglishOfficialLanguage_IsTrue() =>
        Assert.True(_sut.Evaluate(Ctx(firstLanguage: "other", countryLanguages: ["English", "French"])));

    [Fact]
    public void Other_EnglishMatchIsCaseInsensitive() =>
        Assert.True(_sut.Evaluate(Ctx(firstLanguage: "other", countryLanguages: ["english"])));

    // AC 005 (France) / AC 006 (Switzerland): no English official language
    [Fact]
    public void Other_WithoutEnglishOfficialLanguage_IsFalse() =>
        Assert.False(_sut.Evaluate(Ctx(firstLanguage: "other", countryLanguages: ["German", "French", "Italian", "Romansh"])));

    // Fail-safes: unknown languages / unanswered / declined-to-say → mandatory
    [Fact]
    public void Other_WithUnknownCountryLanguages_IsFalse() =>
        Assert.False(_sut.Evaluate(Ctx(firstLanguage: "other", countryLanguages: null)));

    [Theory]
    [InlineData("chose-not-to-say")]
    [InlineData("not-known")]
    public void UnknownFirstLanguage_IsFalse(string lang) =>
        Assert.False(_sut.Evaluate(Ctx(firstLanguage: lang, countryLanguages: ["English"])));

    [Fact]
    public void MissingFirstLanguageAnswer_IsFalse() =>
        Assert.False(_sut.Evaluate(Ctx(firstLanguage: null, countryLanguages: ["English"])));

    // Scoping: any other removal reason (the evidence page is shared) → never optional
    [Theory]
    [InlineData("permanently-excluded")]
    [InlineData(null)]
    public void NonEalReason_IsFalse(string? reason) =>
        Assert.False(_sut.Evaluate(Ctx(reason: reason, firstLanguage: "english")));
}
