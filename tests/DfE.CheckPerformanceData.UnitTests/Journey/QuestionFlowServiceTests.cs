using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class QuestionFlowServiceTests
{
    private readonly QuestionFlowConfig _config;
    private readonly IQuestionFlowBlobClient _blobClient = Substitute.For<IQuestionFlowBlobClient>();
    private readonly QuestionFlowService _sut;

    public QuestionFlowServiceTests()
    {
        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        _sut = new QuestionFlowService(_blobClient, cache);

        _config = new QuestionFlowConfig
        {
            FirstPageId = "reason",
            Pages =
            [
                new JourneyPage
                {
                    Id = "reason",
                    Questions =
                    [
                        new Question
                        {
                            Id = "reason",
                            Type = QuestionType.Radio,
                            Title = "Reason",
                            Options =
                            [
                                new QuestionOption { Value = "social-care", Label = "Social care", NextPageId = "social-care" },
                                new QuestionOption { Value = "other", Label = "Other" }
                            ]
                        }
                    ],
                    NextPageId = "evidence"
                },
                new JourneyPage
                {
                    Id = "social-care",
                    Questions =
                    [
                        new Question { Id = "sat-exams", Type = QuestionType.Radio, Title = "Sat exams?",
                            Options =
                            [
                                new QuestionOption { Value = "yes", Label = "Yes" },
                                new QuestionOption { Value = "no", Label = "No" }
                            ]
                        }
                    ],
                    NextPageId = "evidence"
                },
                new JourneyPage { Id = "evidence", Questions = [new Question { Id = "evidence", Type = QuestionType.FileUpload, Title = "Upload evidence" }] }
            ]
        };
    }

    // ── GetNextPageId ───────────────────────────────────────────────────────

    [Fact]
    public void GetNextPageId_WhenRadioAnswerHasBranch_ReturnsBranchPageId()
    {
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["reason"] = new() { TextValue = "social-care" }
        };

        Assert.Equal("social-care", _sut.GetNextPageId(_config, "reason", answers));
    }

    [Fact]
    public void GetNextPageId_WhenRadioAnswerHasNoBranch_ReturnsPageDefault()
    {
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["reason"] = new() { TextValue = "other" }
        };

        Assert.Equal("evidence", _sut.GetNextPageId(_config, "reason", answers));
    }

    [Fact]
    public void GetNextPageId_WhenNoAnswers_ReturnsPageDefault()
    {
        Assert.Equal("evidence", _sut.GetNextPageId(_config, "reason", new Dictionary<string, QuestionAnswer>()));
    }

    [Fact]
    public void GetNextPageId_WhenPageHasNoNextPageId_ReturnsNull()
    {
        var answers = new Dictionary<string, QuestionAnswer>();

        Assert.Null(_sut.GetNextPageId(_config, "evidence", answers));
    }

    // ── GetNavigationGuard ──────────────────────────────────────────────────

    [Fact]
    public void GetNavigationGuard_WhenPageAlreadyInHistory_ReturnsNull()
    {
        var journey = MakeJourney(history: ["reason"]);

        Assert.Null(_sut.GetNavigationGuard(_config, journey, "reason"));
    }

    [Fact]
    public void GetNavigationGuard_WhenHistoryEmptyAndRequestingFirstPage_ReturnsNull()
    {
        var journey = MakeJourney(history: []);

        Assert.Null(_sut.GetNavigationGuard(_config, journey, "reason"));
    }

    [Fact]
    public void GetNavigationGuard_WhenRequestingCorrectNextPage_ReturnsNull()
    {
        // reason answered with social-care → next page is social-care
        var journey = MakeJourney(
            history: ["reason"],
            answers: new() { ["reason"] = new() { TextValue = "social-care" } });

        Assert.Null(_sut.GetNavigationGuard(_config, journey, "social-care"));
    }

    [Fact]
    public void GetNavigationGuard_WhenSkippingAhead_ReturnsRedirectToExpectedPage()
    {
        // History empty — expected next is "reason"; requesting "evidence" is a skip
        var journey = MakeJourney(history: []);

        var result = Assert.IsType<RedirectToJourneyPage>(_sut.GetNavigationGuard(_config, journey, "evidence"));
        Assert.Equal("reason", result.PageId);
    }

    [Fact]
    public void GetNavigationGuard_WhenJourneyCompleteAndRequestingNewPage_ReturnsRedirectToSummary()
    {
        // All pages answered — GetNextPageId after "evidence" returns null
        var journey = MakeJourney(
            history: ["reason", "social-care", "evidence"],
            answers: new()
            {
                ["reason"] = new() { TextValue = "social-care" },
                ["sat-exams"] = new() { TextValue = "yes" }
            });

        Assert.IsType<RedirectToJourneySummary>(_sut.GetNavigationGuard(_config, journey, "unknown-page"));
    }

    // ── BuildContentKey ─────────────────────────────────────────────────────

    [Fact]
    public void BuildContentKey_IncludesWindowIdAndWhatToChange()
    {
        var windowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var page = new JourneyPage { Id = "info", Type = PageType.Content, Questions = [] };
        var journey = MakeJourney(whatToChange: WhatToChange.Remove);

        var key = _sut.BuildContentKey(windowId, page, new(), journey, _config);

        Assert.StartsWith($"journey-{windowId}-remove", key);
    }

    [Fact]
    public void BuildContentKey_AppendsPrecedingContentKeyRadioAnswers()
    {
        var windowId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        // Make "reason" question a ContentKey radio
        var contentKeyConfig = new QuestionFlowConfig
        {
            FirstPageId = "reason",
            Pages = [
                new JourneyPage
                {
                    Id = "reason",
                    Questions = [new Question { Id = "reason", Type = QuestionType.Radio, Title = "Reason",
                        ContentKey = true,
                        Options = [new QuestionOption { Value = "social-care", Label = "Social care" }] }]
                },
                new JourneyPage { Id = "info", Type = PageType.Content, Questions = [] }
            ]
        };
        var page = contentKeyConfig.Pages[1];
        var answers = new Dictionary<string, QuestionAnswer> { ["reason"] = new() { TextValue = "social-care" } };
        var journey = MakeJourney(history: ["reason"], whatToChange: WhatToChange.Remove, answers: answers);

        var key = _sut.BuildContentKey(windowId, page, answers, journey, contentKeyConfig);

        Assert.EndsWith("-social-care", key);
    }

    // ── GetConfigAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetConfigAsync_WhenBlobDoesNotExist_ReturnsNull()
    {
        _blobClient.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2).Returns((QuestionFlowConfig?)null);

        var result = await _sut.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetConfigAsync_WhenCalledTwice_ReturnsCachedResultWithoutSecondBlobCall()
    {
        _blobClient.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2).Returns((QuestionFlowConfig?)null);

        await _sut.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2);
        await _sut.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2);

        await _blobClient.Received(1).GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static RequestState MakeJourney(
        List<string>? history = null,
        Dictionary<string, QuestionAnswer>? answers = null,
        WhatToChange whatToChange = WhatToChange.Remove) =>
        new()
        {
            SelectedWhatToChange = whatToChange,
            QuestionHistory = history ?? [],
            QuestionAnswers = answers ?? new()
        };
}
