using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#309468: evidence is optional for exactly four first-language answers, regardless of
// the origin country. "English" and "Not known but believed English" stay mandatory.
public sealed class EalEvidenceIsOptionalConditionTests
{
    private readonly EalEvidenceIsOptionalCondition _sut = new();

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

    [Fact] public void Name_IsStable() => Assert.Equal("EalEvidenceIsOptional", _sut.Name);

    // AC 001: these four answers make evidence optional, whatever the origin country is.
    [Theory]
    [InlineData("other", null)]
    [InlineData("other", "English")]
    [InlineData("other", "French")]
    [InlineData("believed-other", null)]
    [InlineData("believed-other", "English")]
    [InlineData("chose-not-to-say", null)]
    [InlineData("not-known", "French")]
    public void OptionalAnswers_AreTrue(string lang, string? countryLanguage) =>
        Assert.True(_sut.Evaluate(Ctx(firstLanguage: lang,
            countryLanguages: countryLanguage is null ? null : [countryLanguage])));

    // AC 002: English and believed-English keep mandatory evidence.
    [Theory]
    [InlineData("english", null)]
    [InlineData("english", "French")]
    [InlineData("believed-english", "English")]
    [InlineData("believed-english", null)]
    public void EnglishAnswers_AreFalse(string lang, string? countryLanguage) =>
        Assert.False(_sut.Evaluate(Ctx(firstLanguage: lang,
            countryLanguages: countryLanguage is null ? null : [countryLanguage])));

    [Fact]
    public void MissingFirstLanguageAnswer_IsFalse() =>
        Assert.False(_sut.Evaluate(Ctx(firstLanguage: null, countryLanguages: ["English"])));

    // Scoping: any other removal reason (the evidence page is shared) → never optional
    [Theory]
    [InlineData("permanently-excluded")]
    [InlineData(null)]
    public void NonEalReason_IsFalse(string? reason) =>
        Assert.False(_sut.Evaluate(Ctx(reason: reason, firstLanguage: "other")));
}
