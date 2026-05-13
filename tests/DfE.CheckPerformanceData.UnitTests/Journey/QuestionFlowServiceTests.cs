using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.QuestionFlow;
using Microsoft.AspNetCore.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class QuestionFlowServiceTests
{
    private readonly QuestionFlowConfig _config;
    private readonly QuestionFlowService _sut;

    public QuestionFlowServiceTests()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(string.Empty);
        _sut = new QuestionFlowService(env);

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

    // ── BuildCurrentPath ────────────────────────────────────────────────────

    [Fact]
    public void BuildCurrentPath_WhenAllRadiosAnswered_ReturnsFullPath()
    {
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["reason"] = new() { TextValue = "social-care" },
            ["sat-exams"] = new() { TextValue = "yes" }
        };

        var path = _sut.BuildCurrentPath(_config, answers);

        Assert.Equal(["reason", "social-care", "evidence"], path);
    }

    [Fact]
    public void BuildCurrentPath_WhenRadioUnanswered_StopsBeforePageWithUnansweredRadio()
    {
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["reason"] = new() { TextValue = "social-care" }
            // sat-exams on the social-care page is not answered
        };

        var path = _sut.BuildCurrentPath(_config, answers);

        // stops before social-care because sat-exams has no answer
        Assert.Equal(["reason"], path);
    }

    [Fact]
    public void BuildCurrentPath_WhenFirstRadioUnanswered_ReturnsEmptyPath()
    {
        var path = _sut.BuildCurrentPath(_config, new Dictionary<string, QuestionAnswer>());

        Assert.Empty(path);
    }

    [Fact]
    public void BuildCurrentPath_TakesDefaultBranchWhenOptionHasNoNextPageId()
    {
        var answers = new Dictionary<string, QuestionAnswer>
        {
            ["reason"] = new() { TextValue = "other" },
            // evidence page has no radios, so it will be included
        };

        var path = _sut.BuildCurrentPath(_config, answers);

        Assert.Equal(["reason", "evidence"], path);
    }

    // ── GetConfig ───────────────────────────────────────────────────────────

    [Fact]
    public void GetConfig_WhenFileDoesNotExist_ReturnsNull()
    {
        var result = _sut.GetConfig(WhatToChange.Merge, CheckingWindowType.KS2);

        Assert.Null(result);
    }

    [Fact]
    public void GetConfig_WhenCalledTwice_ReturnsCachedNull()
    {
        var first = _sut.GetConfig(WhatToChange.Merge, CheckingWindowType.KS2);
        var second = _sut.GetConfig(WhatToChange.Merge, CheckingWindowType.KS2);

        Assert.Null(first);
        Assert.Null(second);
    }
}
