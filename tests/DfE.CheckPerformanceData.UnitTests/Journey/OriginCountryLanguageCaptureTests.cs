using DfE.CheckPerformanceData.Application.Countries;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public sealed class OriginCountryLanguageCaptureTests
{
    private readonly IRulesConfigService _rulesConfig = Substitute.For<IRulesConfigService>();
    private readonly ICountryService _countries = Substitute.For<ICountryService>();
    private readonly OriginCountryLanguageCapture _sut;

    public OriginCountryLanguageCaptureTests()
    {
        var lookups = new Lookups(new Dictionary<string, IReadOnlyList<string>>
        {
            ["FR"] = ["French"],
            ["CA"] = ["English", "French"],
        });
        _rulesConfig.GetLookupsAsync(Arg.Any<CancellationToken>()).Returns((lookups, (string?)"etag"));
        _sut = new OriginCountryLanguageCapture(_rulesConfig, _countries, NullLogger<OriginCountryLanguageCapture>.Instance);
    }

    private static Dictionary<string, QuestionAnswer> CountryAnswer(string? text, string? code) => new()
    {
        ["country-originally-from"] = new QuestionAnswer { TextValue = text, CodeValue = code }
    };

    [Fact]
    public async Task NoCountryAnswer_LeavesStateUntouched()
    {
        var journey = new RequestState { OriginCountryCode = "FR", OriginCountryLanguages = ["French"] };
        await _sut.ApplyAsync(journey, new Dictionary<string, QuestionAnswer>());
        Assert.Equal("FR", journey.OriginCountryCode);
    }

    [Fact]
    public async Task CodePresent_StoresCodeAndLanguages()
    {
        var journey = new RequestState();
        await _sut.ApplyAsync(journey, CountryAnswer("Canada", "CA"));
        Assert.Equal("CA", journey.OriginCountryCode);
        Assert.Equal(["English", "French"], journey.OriginCountryLanguages);
    }

    [Fact]
    public async Task CodeMissing_ResolvesByExactName_AndBackfillsCodeValue()
    {
        _countries.GetCodeByNameAsync("France", Arg.Any<CancellationToken>()).Returns("FR");
        var journey = new RequestState();
        var answers = CountryAnswer("France", null);
        await _sut.ApplyAsync(journey, answers);
        Assert.Equal("FR", journey.OriginCountryCode);
        Assert.Equal(["French"], journey.OriginCountryLanguages);
        Assert.Equal("FR", answers["country-originally-from"].CodeValue); // engine gets the code again
    }

    [Fact]
    public async Task UnresolvableCountry_ClearsState()
    {
        _countries.GetCodeByNameAsync("Atlantis", Arg.Any<CancellationToken>()).Returns((string?)null);
        var journey = new RequestState { OriginCountryCode = "FR", OriginCountryLanguages = ["French"] };
        await _sut.ApplyAsync(journey, CountryAnswer("Atlantis", null));
        Assert.Null(journey.OriginCountryCode);
        Assert.Null(journey.OriginCountryLanguages);
    }

    [Fact]
    public async Task CountryNotInLookup_StoresCodeWithNullLanguages()
    {
        var journey = new RequestState();
        await _sut.ApplyAsync(journey, CountryAnswer("Belgium", "BE"));
        Assert.Equal("BE", journey.OriginCountryCode);
        Assert.Null(journey.OriginCountryLanguages);
    }

    [Fact]
    public async Task LookupBlobFailure_IsSwallowed_LanguagesNull()
    {
        _rulesConfig.GetLookupsAsync(Arg.Any<CancellationToken>())
            .Returns<Task<(Lookups, string?)>>(_ => throw new InvalidOperationException("blob down"));
        var journey = new RequestState();
        await _sut.ApplyAsync(journey, CountryAnswer("Canada", "CA"));
        Assert.Equal("CA", journey.OriginCountryCode);
        Assert.Null(journey.OriginCountryLanguages); // fail-safe: unknown languages → evidence stays mandatory
    }
}
