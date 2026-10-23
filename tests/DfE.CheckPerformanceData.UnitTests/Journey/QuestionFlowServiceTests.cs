using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using CheckingWindowDto = DfE.CheckPerformanceData.Application.LandingPage.CheckingWindowDto;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class QuestionFlowServiceTests
{
    private readonly QuestionFlowConfig _config;
    private readonly IQuestionFlowConfigSource _blobClient = Substitute.For<IQuestionFlowConfigSource>();
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

    // ── GetReachableEvidencePage ────────────────────────────────────────────

    [Fact]
    public void GetReachableEvidencePage_LinearFlow_ReturnsEvidencePage()
    {
        var config = new QuestionFlowConfig
        {
            FirstPageId = "select-pupil",
            Pages =
            [
                new JourneyPage { Id = "select-pupil", Type = PageType.PupilSearch, Questions = [], NextPageId = "evidence" },
                new JourneyPage { Id = "evidence", Type = PageType.EvidenceUpload, Questions = [] }
            ]
        };

        var result = _sut.GetReachableEvidencePage(config, new Dictionary<string, QuestionAnswer>());

        Assert.NotNull(result);
        Assert.Equal("evidence", result!.Id);
    }

    [Fact]
    public void GetReachableEvidencePage_BranchSelectsOptionA_ReturnsBranchAEvidencePage()
    {
        var config = BranchingConfig();
        var answers = new Dictionary<string, QuestionAnswer> { ["reason"] = new() { TextValue = "option-a" } };

        var result = _sut.GetReachableEvidencePage(config, answers);

        Assert.NotNull(result);
        Assert.Equal("evidence-a", result!.Id);
    }

    [Fact]
    public void GetReachableEvidencePage_BranchSelectsOptionB_ReturnsBranchBEvidencePage()
    {
        var config = BranchingConfig();
        var answers = new Dictionary<string, QuestionAnswer> { ["reason"] = new() { TextValue = "option-b" } };

        var result = _sut.GetReachableEvidencePage(config, answers);

        Assert.NotNull(result);
        Assert.Equal("evidence-b", result!.Id);
    }

    [Fact]
    public void GetReachableEvidencePage_NoEvidencePageInFlow_ReturnsNull()
    {
        var config = new QuestionFlowConfig
        {
            FirstPageId = "select-pupil",
            Pages =
            [
                new JourneyPage { Id = "select-pupil", Type = PageType.PupilSearch, Questions = [], NextPageId = "summary" },
                new JourneyPage { Id = "summary", Type = PageType.Content, Questions = [] }
            ]
        };

        Assert.Null(_sut.GetReachableEvidencePage(config, new Dictionary<string, QuestionAnswer>()));
    }

    [Fact]
    public void GetReachableEvidencePage_DanglingNextPageId_ReturnsNull()
    {
        var config = new QuestionFlowConfig
        {
            FirstPageId = "select-pupil",
            Pages =
            [
                new JourneyPage { Id = "select-pupil", Type = PageType.PupilSearch, Questions = [], NextPageId = "does-not-exist" }
            ]
        };

        Assert.Null(_sut.GetReachableEvidencePage(config, new Dictionary<string, QuestionAnswer>()));
    }

    [Fact]
    public void GetReachableEvidencePage_CyclicFlow_ReturnsNull()
    {
        var config = new QuestionFlowConfig
        {
            FirstPageId = "page-a",
            Pages =
            [
                new JourneyPage { Id = "page-a", Questions = [], NextPageId = "page-b" },
                new JourneyPage { Id = "page-b", Questions = [], NextPageId = "page-a" }
            ]
        };

        Assert.Null(_sut.GetReachableEvidencePage(config, new Dictionary<string, QuestionAnswer>()));
    }

    private static QuestionFlowConfig BranchingConfig() => new()
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
                            new QuestionOption { Value = "option-a", Label = "Option A", NextPageId = "path-a" },
                            new QuestionOption { Value = "option-b", Label = "Option B", NextPageId = "path-b" }
                        ]
                    }
                ]
            },
            new JourneyPage { Id = "path-a", Questions = [], NextPageId = "evidence-a" },
            new JourneyPage { Id = "evidence-a", Type = PageType.EvidenceUpload, Questions = [] },
            new JourneyPage { Id = "path-b", Questions = [], NextPageId = "evidence-b" },
            new JourneyPage { Id = "evidence-b", Type = PageType.EvidenceUpload, Questions = [] }
        ]
    };

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
    public void BuildContentKey_IncludesWindowTypeAndWhatToChange()
    {
        var page = new JourneyPage { Id = "info", Type = PageType.Content, Questions = [] };
        var journey = MakeJourney(whatToChange: WhatToChange.Remove, windowType: CheckingWindowType.KS4June);

        var key = _sut.BuildContentKey(page, new(), journey, _config);

        Assert.StartsWith("journey-ks4june-remove", key);
    }

    [Fact]
    public void BuildContentKey_IsTheSameForTwoWindowsOfOneType()
    {
        // The guidance is copied between environments, where the windows have other ids. A key
        // that named the window id would orphan every block on the way.
        var page = new JourneyPage { Id = "info", Type = PageType.EvidenceUpload, Questions = [] };
        var first = MakeJourney(windowType: CheckingWindowType.Post16);
        var second = MakeJourney(windowType: CheckingWindowType.Post16);

        Assert.NotEqual(first.CheckingWindow!.Id, second.CheckingWindow!.Id);
        Assert.Equal(
            _sut.BuildContentKey(page, new(), first, _config),
            _sut.BuildContentKey(page, new(), second, _config));
    }

    [Fact]
    public void BuildContentKey_AppendsPrecedingContentKeyRadioAnswers()
    {
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

        var key = _sut.BuildContentKey(page, answers, journey, contentKeyConfig);

        Assert.EndsWith("-social-care", key);
    }

    // ── BuildEvidenceContentBlocks (#591) ─────────────────────────────────────────────

    // The KS4 June social care shape: a contentKey radio, then a contentKey checkbox, then the
    // evidence page.
    private static QuestionFlowConfig CheckboxContentKeyConfig() => new()
    {
        FirstPageId = "reason",
        Pages =
        [
            new JourneyPage
            {
                Id = "reason",
                Questions = [new Question { Id = "reason", Type = QuestionType.Radio, Title = "Reason",
                    ContentKey = true,
                    Options = [new QuestionOption { Value = "social-care-involvement", Label = "Social care" }] }]
            },
            new JourneyPage
            {
                Id = "social-care",
                Title = "Social care involvement",
                Questions = [new Question { Id = "social-care-reason", Type = QuestionType.Checkbox, Title = "What happened?",
                    ContentKey = true,
                    Options =
                    [
                        new QuestionOption { Value = "social-care-situation", Label = "Social care situation" },
                        new QuestionOption { Value = "police-involvement", Label = "Police involvement" },
                        new QuestionOption { Value = "detained-in-prison", Label = "Detained in prison" }
                    ] }]
            },
            new JourneyPage { Id = "evidence", Type = PageType.EvidenceUpload, Questions = [] }
        ]
    };

    private List<string> KeysFor(params string[] ticked) => [.. BlocksFor(ticked).Select(b => b.Key)];

    private List<EvidenceContentBlock> BlocksFor(params string[] ticked)
    {
        var config = CheckboxContentKeyConfig();
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["reason"] = new() { TextValue = "social-care-involvement" },
            ["social-care-reason"] = new() { SelectedValues = [.. ticked] }
        };
        var journey = MakeJourney(history: ["reason", "social-care", "evidence"],
            whatToChange: WhatToChange.Remove, windowType: CheckingWindowType.KS4June, answers: answers);

        return [.. _sut.BuildEvidenceContentBlocks(config.Pages[2], answers, journey, config)];
    }

    [Fact]
    public void BuildEvidenceContentBlocks_OneTickedOption_GivesTheKeyARadioGaveBefore()
    {
        // The blocks editors wrote for the radio must keep showing: one tick, same key.
        Assert.Equal(
            "journey-ks4june-remove-social-care-involvement-police-involvement",
            KeysFor("police-involvement")[0]);
    }

    [Fact]
    public void BuildEvidenceContentBlocks_SeveralTickedOptions_GiveOneKeyEach_InConfigOrder_ThenTheSharedKey()
    {
        Assert.Equal(
            [
                "journey-ks4june-remove-social-care-involvement-social-care-situation",
                "journey-ks4june-remove-social-care-involvement-detained-in-prison",
                "journey-ks4june-remove-social-care-involvement"
            ],
            KeysFor("detained-in-prison", "social-care-situation"));
    }

    [Theory]
    [InlineData("social-care-situation")]
    [InlineData("police-involvement", "detained-in-prison")]
    [InlineData("social-care-situation", "police-involvement", "detained-in-prison")]
    public void BuildEvidenceContentBlocks_AContentKeyCheckbox_AlwaysEndsWithItsSharedBlock(params string[] ticked)
    {
        // #633: the section has general guidance that shows whatever the school ticks, below
        // the guidance for each ticked option. Its key is the key before the checkbox.
        var last = BlocksFor(ticked)[^1];

        Assert.Equal(
            new EvidenceContentBlock("journey-ks4june-remove-social-care-involvement", null, "Social care involvement"),
            last);
    }

    [Fact]
    public void BuildEvidenceContentBlocks_NameTheOptionEachBlockIsFor()
    {
        // The label goes into a new block's default text, so a guidance writer can tell the
        // blocks apart.
        Assert.Equal(
            ["Social care situation", "Detained in prison", null],
            BlocksFor("detained-in-prison", "social-care-situation").Select(b => b.OptionLabel));
    }

    [Fact]
    public void BuildEvidenceContentBlocks_SharedBlock_FallsBackToTheQuestionTitle_WhenThePageHasNoTitle()
    {
        var config = CheckboxContentKeyConfig();
        var socialCare = config.Pages[1];
        config.Pages[1] = new JourneyPage { Id = socialCare.Id, Questions = socialCare.Questions };
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["reason"] = new() { TextValue = "social-care-involvement" },
            ["social-care-reason"] = new() { SelectedValues = ["police-involvement"] }
        };
        var journey = MakeJourney(history: ["reason", "social-care", "evidence"],
            whatToChange: WhatToChange.Remove, windowType: CheckingWindowType.KS4June, answers: answers);

        var blocks = _sut.BuildEvidenceContentBlocks(config.Pages[2], answers, journey, config);

        Assert.Equal("What happened?", blocks[^1].SectionLabel);
    }

    [Fact]
    public void BuildEvidenceContentBlocks_WithNoContentKeyCheckbox_IsTheSingleContentKey_ForNoOption()
    {
        var page = new JourneyPage { Id = "info", Type = PageType.EvidenceUpload, Questions = [] };
        var journey = MakeJourney(whatToChange: WhatToChange.Remove, windowType: CheckingWindowType.KS4June);

        Assert.Equal(
            [new EvidenceContentBlock(_sut.BuildContentKey(page, new(), journey, _config), null)],
            _sut.BuildEvidenceContentBlocks(page, new(), journey, _config));
    }

    // ── ResolveRequestType ──────────────────────────────────────────────────

    [Fact]
    public void ResolveRequestType_WhenUseAsRequestTypeQuestion_ReturnsItsLabel()
    {
        var config = new QuestionFlowConfig
        {
            FirstPageId = "reason",
            Pages = [
                new JourneyPage
                {
                    Id = "reason",
                    Questions = [new Question { Id = "reason", Type = QuestionType.Radio, Title = "Reason",
                        UseAsRequestType = true,
                        Options = [new QuestionOption { Value = "social-care", Label = "Social care involvement" }] }]
                }
            ]
        };
        var journey = MakeJourney(
            history: ["reason"],
            answers: new() { ["reason"] = new() { TextValue = "social-care" } });

        Assert.Equal("Social care involvement", _sut.ResolveRequestType(config, journey));
    }

    [Fact]
    public void ResolveRequestType_WhenNoUseAsRequestTypeQuestion_FallsBackToFirstAnsweredQuestion()
    {
        var config = new QuestionFlowConfig
        {
            FirstPageId = "reason",
            Pages = [
                new JourneyPage
                {
                    Id = "reason",
                    Questions = [new Question { Id = "reason", Type = QuestionType.Radio, Title = "Reason",
                        Options = [new QuestionOption { Value = "other", Label = "Other reason" }] }]
                }
            ]
        };
        var journey = MakeJourney(
            history: ["reason"],
            answers: new() { ["reason"] = new() { TextValue = "other" } });

        Assert.Equal("Other reason", _sut.ResolveRequestType(config, journey));
    }

    [Fact]
    public void ResolveRequestType_SkipsUseAsRequestTypeQuestionWithNoAnswer()
    {
        var config = new QuestionFlowConfig
        {
            FirstPageId = "p1",
            Pages = [
                new JourneyPage
                {
                    Id = "p1",
                    Questions = [
                        new Question { Id = "q-flag", Type = QuestionType.Radio, Title = "Flagged",
                            UseAsRequestType = true,
                            Options = [new QuestionOption { Value = "x", Label = "X" }] },
                        new Question { Id = "q-other", Type = QuestionType.Radio, Title = "Other",
                            Options = [new QuestionOption { Value = "y", Label = "Y" }] }
                    ]
                }
            ]
        };
        // flagged question has no answer — should fall back to first answered question
        var journey = MakeJourney(
            history: ["p1"],
            answers: new() { ["q-other"] = new() { TextValue = "y" } });

        Assert.Equal("Y", _sut.ResolveRequestType(config, journey));
    }

    [Fact]
    public void ResolveRequestType_WhenNoAnswers_ReturnsEmptyString()
    {
        var journey = MakeJourney(history: ["reason"]);

        Assert.Equal(string.Empty, _sut.ResolveRequestType(_config, journey));
    }

    // AB#297310: the Add journey's learner-details page has no useAsRequestType question — its
    // first question is the pupil's own typed first name. Without a guard, the generic
    // "fall back to the first answered question" rule would surface it as if it were a request
    // category (observed live: "Add - Alice"), which is meaningless — an Add request has no
    // sub-category the way Remove has "reason".
    [Fact]
    public void ResolveRequestType_WhenHistoryIncludesAPupilFromAnswersPage_ReturnsEmptyString()
    {
        var config = new QuestionFlowConfig
        {
            FirstPageId = "learner-details",
            Pages =
            [
                new JourneyPage
                {
                    Id = "learner-details",
                    PupilFromAnswers = true,
                    Questions = [new Question { Id = "first-name", Type = QuestionType.FreeText, Title = "First name" }],
                    NextPageId = "admission-details"
                },
                new JourneyPage
                {
                    Id = "admission-details",
                    Questions = [new Question { Id = "year-group", Type = QuestionType.Radio, Title = "Year group",
                        Options = [new QuestionOption { Value = "10", Label = "Year 10" }] }]
                }
            ]
        };
        var journey = MakeJourney(
            history: ["learner-details", "admission-details"],
            answers: new()
            {
                ["first-name"] = new() { TextValue = "Alice" },
                ["year-group"] = new() { TextValue = "10" }
            });

        Assert.Equal(string.Empty, _sut.ResolveRequestType(config, journey));
    }

    // ── ResolveRequestTypeValue ─────────────────────────────────────────────

    [Fact]
    public void ResolveRequestTypeValue_WhenUseAsRequestTypeQuestion_ReturnsItsRawValue()
    {
        var config = new QuestionFlowConfig
        {
            FirstPageId = "reason",
            Pages = [
                new JourneyPage
                {
                    Id = "reason",
                    Questions = [new Question { Id = "reason", Type = QuestionType.Radio, Title = "Reason",
                        UseAsRequestType = true,
                        Options = [new QuestionOption { Value = "social-care-involvement", Label = "Social care involvement" }] }]
                }
            ]
        };
        var journey = MakeJourney(
            history: ["reason"],
            answers: new() { ["reason"] = new() { TextValue = "social-care-involvement" } });

        Assert.Equal("social-care-involvement", _sut.ResolveRequestTypeValue(config, journey));
    }

    [Fact]
    public void ResolveRequestTypeValue_WhenNoUseAsRequestTypeQuestion_ReturnsEmpty_NoFallback()
    {
        // Unlike ResolveRequestType (display), the contract value must not fall back to
        // the first answered question — flows without a flagged question are bare-prefix.
        var config = new QuestionFlowConfig
        {
            FirstPageId = "reason",
            Pages = [
                new JourneyPage
                {
                    Id = "reason",
                    Questions = [new Question { Id = "reason", Type = QuestionType.Radio, Title = "Reason",
                        Options = [new QuestionOption { Value = "other", Label = "Other reason" }] }]
                }
            ]
        };
        var journey = MakeJourney(
            history: ["reason"],
            answers: new() { ["reason"] = new() { TextValue = "other" } });

        Assert.Equal(string.Empty, _sut.ResolveRequestTypeValue(config, journey));
    }

    [Fact]
    public void ResolveRequestTypeValue_WhenFlaggedQuestionUnanswered_ReturnsEmpty()
    {
        var config = new QuestionFlowConfig
        {
            FirstPageId = "p1",
            Pages = [
                new JourneyPage
                {
                    Id = "p1",
                    Questions = [
                        new Question { Id = "q-flag", Type = QuestionType.Radio, Title = "Flagged",
                            UseAsRequestType = true,
                            Options = [new QuestionOption { Value = "x", Label = "X" }] },
                        new Question { Id = "q-other", Type = QuestionType.Radio, Title = "Other",
                            Options = [new QuestionOption { Value = "y", Label = "Y" }] }
                    ]
                }
            ]
        };
        var journey = MakeJourney(
            history: ["p1"],
            answers: new() { ["q-other"] = new() { TextValue = "y" } });

        Assert.Equal(string.Empty, _sut.ResolveRequestTypeValue(config, journey));
    }

    [Fact]
    public void ResolveRequestTypeValue_WhenNoAnswers_ReturnsEmptyString()
    {
        var journey = MakeJourney(history: ["reason"]);

        Assert.Equal(string.Empty, _sut.ResolveRequestTypeValue(_config, journey));
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
    public async Task GetConfigAsync_WhenBlobReturnsConfig_CachesResultAndSkipsSecondBlobCall()
    {
        _blobClient.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2).Returns(_config);

        await _sut.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2);
        await _sut.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2);

        await _blobClient.Received(1).GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2);
    }

    [Fact]
    public async Task GetConfigAsync_WhenBlobReturnsNull_DoesNotCacheNull_SoLaterUploadIsPickedUp()
    {
        // First lookup misses (config not yet uploaded to blob storage).
        _blobClient.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2).Returns((QuestionFlowConfig?)null);
        var first = await _sut.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2);
        Assert.Null(first);

        // The config is subsequently uploaded. A permanently-cached null would keep returning
        // null (the Preprod bug); instead the next lookup must re-query the blob and find it.
        _blobClient.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2).Returns(_config);
        var second = await _sut.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2);

        Assert.Same(_config, second);
        await _blobClient.Received(2).GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS2);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static RequestState MakeJourney(
        List<string>? history = null,
        Dictionary<string, QuestionAnswer>? answers = null,
        WhatToChange whatToChange = WhatToChange.Remove,
        CheckingWindowType windowType = CheckingWindowType.KS4June) =>
        new()
        {
            SelectedWhatToChange = whatToChange,
            CheckingWindow = new CheckingWindowDto
            {
                Id = Guid.NewGuid(),
                Title = "Test Window",
                KeyStage = KeyStages.KS4,
                CheckingWindowType = windowType,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(13)
            },
            QuestionHistory = history ?? [],
            QuestionAnswers = answers ?? new()
        };

    // ── GetPupilDataChangesAsync (docs/ks2-journeys.md, decision 1) ─────────

    private static readonly QuestionFlowConfig AnyFlow = new() { FirstPageId = "p", Pages = [] };

    [Fact]
    public async Task GetPupilDataChangesAsync_ReturnsTheChangesThatHaveAFlowFile_InDisplayOrder()
    {
        _blobClient.GetConfigAsync(WhatToChange.Remove, CheckingWindowType.KS4June).Returns(AnyFlow);
        _blobClient.GetConfigAsync(WhatToChange.Add, CheckingWindowType.KS4June).Returns(AnyFlow);
        _blobClient.GetConfigAsync(WhatToChange.Merge, CheckingWindowType.KS4June).Returns(AnyFlow);
        _blobClient.GetConfigAsync(WhatToChange.Include, CheckingWindowType.KS4June).Returns(AnyFlow);

        var changes = await _sut.GetPupilDataChangesAsync(CheckingWindowType.KS4June);

        Assert.Equal([WhatToChange.Merge, WhatToChange.Include, WhatToChange.Remove, WhatToChange.Add], changes);
    }

    [Fact]
    public async Task GetPupilDataChangesAsync_LeavesOutAChangeWithNoFlowFile()
    {
        _blobClient.GetConfigAsync(WhatToChange.Remove, CheckingWindowType.KS2).Returns(AnyFlow);

        var changes = await _sut.GetPupilDataChangesAsync(CheckingWindowType.KS2);

        Assert.Equal([WhatToChange.Remove], changes);
    }

    [Fact]
    public async Task GetPupilDataChangesAsync_NeverOffersAResultsChange()
    {
        // The results changes start from the results-enquiry page, not from What to change.
        _blobClient.GetConfigAsync(WhatToChange.IncorrectGrade, CheckingWindowType.Post16).Returns(AnyFlow);
        _blobClient.GetConfigAsync(WhatToChange.MissingQualification, CheckingWindowType.Post16).Returns(AnyFlow);
        _blobClient.GetConfigAsync(WhatToChange.ResultDoesNotBelong, CheckingWindowType.Post16).Returns(AnyFlow);

        var changes = await _sut.GetPupilDataChangesAsync(CheckingWindowType.Post16);

        Assert.Empty(changes);
    }
}
