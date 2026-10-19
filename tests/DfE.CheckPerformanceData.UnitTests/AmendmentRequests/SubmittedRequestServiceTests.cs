using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.Logging;
using DfE.CheckPerformanceData.Application.UnitTests.Common;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public class SubmittedRequestServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string Reference = "CYPMD_KS4June_ABC1234";

    private readonly IRequestStateBlobClient _blob = Substitute.For<IRequestStateBlobClient>();
    private readonly IQuestionFlowService _flow = Substitute.For<IQuestionFlowService>();
    private readonly IRequestRepository _requestRepo = Substitute.For<IRequestRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly RecordingLogger<SubmittedRequestService> _logger = new();
    private readonly SubmittedRequestService _sut;

    public SubmittedRequestServiceTests()
    {
        _currentUser.OrganisationUrn.Returns("100000");
        _currentUser.UserId.Returns("user-77");
        _sut = new SubmittedRequestService(_blob, _flow, _requestRepo, _currentUser, _logger);
    }

    [Fact]
    public async Task GetAsync_WhenNoJourneyStored_ReturnsNull()
    {
        StubRow(RequestStatus.Submitted);
        _blob.GetAsync(WindowId, Reference).Returns((RequestState?)null);

        Assert.Null(await _sut.GetAsync(WindowId, Reference));
    }

    [Fact]
    public async Task GetAsync_WhenConfigNotFound_ReturnsNull()
    {
        StubRow(RequestStatus.Submitted);
        _blob.GetAsync(WindowId, Reference).Returns(Journey());
        _flow.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>())
            .Returns((QuestionFlowConfig?)null);

        Assert.Null(await _sut.GetAsync(WindowId, Reference));
    }

    [Fact]
    public async Task GetAsync_WhenCallerOrgDoesNotOwnRequest_ReturnsNullWithoutReadingBlob()
    {
        // No ChangeRequests row for this org/reference → cross-tenant access attempt.
        _requestRepo.GetAmendmentRequestAsync(WindowId, 100000L, Reference)
            .Returns((AmendmentRequestData?)null);

        Assert.Null(await _sut.GetAsync(WindowId, Reference));
        await _blob.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<string>());
    }

    // A missing row under the caller's URN is the shape of a school trying another school's
    // URL, so it is logged under the one access-refusal event (#612). The negative half is the
    // real pin: the reference is the project's accepted log identifier, the pupil's name is not,
    // and a template that added it "for debuggability" would land PII in Logit.
    [Fact]
    public async Task GetAsync_WhenCallerOrgDoesNotOwnRequest_LogsOneAccessRefusalWithoutPupilData()
    {
        _requestRepo.GetAmendmentRequestAsync(WindowId, 100000L, Reference)
            .Returns((AmendmentRequestData?)null);

        await _sut.GetAsync(WindowId, Reference);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(AccessRefusalLog.AccessRefused, entry.EventId);
        Assert.Contains("user-77", entry.Message);
        Assert.Contains("100000", entry.Message);
        Assert.Contains(Reference, entry.Message);
        Assert.Contains(WindowId.ToString(), entry.Message);
        Assert.DoesNotContain("Jane", entry.Message);
        Assert.DoesNotContain("Smith", entry.Message);
    }

    // The school owns the row, so nothing is refused — even when the journey blob behind it is
    // missing and the method still returns null. Only the ownership check is an access decision.
    [Fact]
    public async Task GetAsync_WhenRowFound_LogsNoAccessRefusal()
    {
        StubRow(RequestStatus.Submitted);
        _blob.GetAsync(WindowId, Reference).Returns((RequestState?)null);

        await _sut.GetAsync(WindowId, Reference);

        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task GetConfirmDataCorrectAsync_WhenRowMissing_LogsOneAccessRefusal()
    {
        _requestRepo.GetConfirmDataCorrectAsync(WindowId, 100000L, Reference)
            .Returns((ConfirmDataCorrectData?)null);

        await _sut.GetConfirmDataCorrectAsync(WindowId, Reference);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(AccessRefusalLog.AccessRefused, entry.EventId);
        Assert.Contains(Reference, entry.Message);
    }

    [Fact]
    public async Task GetAsync_MapsAnswerRowsPupilNameAndSubmittedBy()
    {
        var submittedAt = new DateTime(2026, 6, 16, 9, 30, 0);
        var journey = Journey();
        journey.QuestionAnswers["q1"] = new QuestionAnswer { TextValue = "left-england" };
        var page = new JourneyPage
        {
            Id = "reason",
            Questions =
            [
                new Question
                {
                    Id = "q1",
                    Type = QuestionType.Radio,
                    Title = "Why are you removing this pupil?",
                    Options = [new QuestionOption { Value = "left-england", Label = "Permanently left England" }]
                }
            ]
        };
        Setup(journey, page);
        StubRow(RequestStatus.Submitted, "submitter@education.gov.uk", submittedAt);

        var result = await _sut.GetAsync(WindowId, Reference);

        Assert.NotNull(result);
        Assert.Equal(WhatToChange.Remove, result!.WhatToChange);
        Assert.Equal("Jane Smith", result.PupilName);
        Assert.Equal("CYPMD123", result.PupilCypmdId);
        Assert.Single(result.Rows);
        Assert.Equal("Why are you removing this pupil?", result.Rows[0].Title);
        Assert.Equal("Permanently left England", result.Rows[0].DisplayValue);
        Assert.Equal("submitter@education.gov.uk", result.SubmittedByEmail);
        Assert.Equal(submittedAt, result.SubmittedAt);
        Assert.Equal(Reference, result.ReferenceNumber);
        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task GetAsync_MapsUploadedFileRows()
    {
        var journey = Journey();
        journey.QuestionAnswers["q1"] = new QuestionAnswer
        {
            FileValues =
            [
                new FileAnswer
                {
                    StoredFileName = "11111111-1111-1111-1111-111111111111",
                    OriginalFileName = "evidence.pdf",
                    PageCount = 2,
                    FileSizeBytes = 2048
                }
            ]
        };
        var page = new JourneyPage
        {
            Id = "reason",
            Questions = [new Question { Id = "q1", Type = QuestionType.FileUpload, Title = "Upload evidence" }]
        };
        Setup(journey, page);

        var result = await _sut.GetAsync(WindowId, Reference);

        Assert.NotNull(result);
        Assert.Empty(result!.Rows);
        Assert.Single(result.Files);
        Assert.Equal("evidence.pdf", result.Files[0].OriginalFileName);
        Assert.Equal("11111111-1111-1111-1111-111111111111", result.Files[0].StoredFileName);
        Assert.Equal(2048, result.Files[0].FileSizeBytes);
    }

    [Fact]
    public async Task GetAsync_SkipsContentAndPupilSearchPages()
    {
        StubRow(RequestStatus.Submitted);
        var journey = Journey(history: ["intro", "select-pupil", "reason"]);
        journey.QuestionAnswers["q1"] = new QuestionAnswer { TextValue = "left-england" };
        var reasonPage = new JourneyPage
        {
            Id = "reason",
            Questions =
            [
                new Question
                {
                    Id = "q1", Type = QuestionType.Radio, Title = "Why?",
                    Options = [new QuestionOption { Value = "left-england", Label = "Left England" }]
                }
            ]
        };
        _blob.GetAsync(WindowId, Reference).Returns(journey);
        _flow.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(Config(reasonPage));
        _flow.GetPage(Arg.Any<QuestionFlowConfig>(), "intro")
            .Returns(new JourneyPage { Id = "intro", Type = PageType.Content });
        _flow.GetPage(Arg.Any<QuestionFlowConfig>(), "select-pupil")
            .Returns(new JourneyPage { Id = "select-pupil", Type = PageType.PupilSearch });
        _flow.GetPage(Arg.Any<QuestionFlowConfig>(), "reason").Returns(reasonPage);

        var result = await _sut.GetAsync(WindowId, Reference);

        Assert.NotNull(result);
        Assert.Single(result!.Rows);
        Assert.Equal("Left England", result.Rows[0].DisplayValue);
    }

    [Fact]
    public async Task GetAsync_IncludesStatusFromChangeRequestRow()
    {
        var journey = Journey();
        var page = new JourneyPage
        {
            Id = "reason",
            Questions = [new Question { Id = "q1", Type = QuestionType.FileUpload, Title = "Upload evidence" }]
        };
        Setup(journey, page);
        _requestRepo.GetAmendmentRequestAsync(WindowId, 100000L, Reference).Returns(new AmendmentRequestData
        {
            PupilFirstname = "Jane",
            PupilSurname = "Smith",
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove",
            Status = RequestStatus.Withdrawn,
            ReferenceNumber = Reference
        });

        var result = await _sut.GetAsync(WindowId, Reference);

        Assert.NotNull(result);
        Assert.Equal(RequestStatus.Withdrawn, result!.Status);
    }

    [Fact]
    public async Task GetAsync_WhenDraftJourneyUnstamped_UsesSavedByDetailsFromRow()
    {
        var savedAt = new DateTime(2026, 6, 17, 14, 0, 0);
        var journey = Journey(); // draft: no SubmittedAt / email on the blob
        var page = new JourneyPage
        {
            Id = "reason",
            Questions = [new Question { Id = "q1", Type = QuestionType.FileUpload, Title = "Upload evidence" }]
        };
        Setup(journey, page);
        StubRow(RequestStatus.InProgress, "saver@education.gov.uk", savedAt);

        var result = await _sut.GetAsync(WindowId, Reference);

        Assert.NotNull(result);
        Assert.Equal(RequestStatus.InProgress, result!.Status);
        Assert.Equal("saver@education.gov.uk", result.SubmittedByEmail);
        Assert.Equal(savedAt, result.SubmittedAt);
    }

    [Fact]
    public async Task GetConfirmDataCorrectAsync_WhenRowMissing_ReturnsNull()
    {
        _requestRepo.GetConfirmDataCorrectAsync(WindowId, 100000L, Reference)
            .Returns((ConfirmDataCorrectData?)null);

        Assert.Null(await _sut.GetConfirmDataCorrectAsync(WindowId, Reference));
    }

    [Fact]
    public async Task GetConfirmDataCorrectAsync_WhenRowIsAmendment_ReturnsNull()
    {
        _requestRepo.GetConfirmDataCorrectAsync(WindowId, 100000L, Reference).Returns(new ConfirmDataCorrectData
        {
            RequestType = RequestType.Amendment,
            Status = RequestStatus.Submitted,
            SubmittedByEmail = "submitter@education.gov.uk",
            Submitted = DateTime.Now,
            ReferenceNumber = Reference
        });

        Assert.Null(await _sut.GetConfirmDataCorrectAsync(WindowId, Reference));
        // The school owns the row; a row of another type is not a refusal.
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task GetConfirmDataCorrectAsync_WhenConfirmCorrect_MapsView()
    {
        var submitted = new DateTime(2026, 6, 16, 9, 30, 0);
        _requestRepo.GetConfirmDataCorrectAsync(WindowId, 100000L, Reference).Returns(new ConfirmDataCorrectData
        {
            RequestType = RequestType.ConfirmCorrect,
            Status = RequestStatus.Withdrawn,
            SubmittedByEmail = "submitter@education.gov.uk",
            Submitted = submitted,
            ReferenceNumber = Reference
        });

        var result = await _sut.GetConfirmDataCorrectAsync(WindowId, Reference);

        Assert.NotNull(result);
        Assert.Equal(RequestStatus.Withdrawn, result!.Status);
        Assert.Equal("submitter@education.gov.uk", result.SubmittedByEmail);
        Assert.Equal(submitted, result.SubmittedAt);
        Assert.Equal(Reference, result.ReferenceNumber);
    }

    [Fact]
    public async Task GetAsync_MergeJourney_PopulatesRecordDisplays()
    {
        var journey = Journey();
        journey.MatchedPupil = new PupilDto
        {
            Id = Guid.NewGuid(),
            Firstname = "John",
            Surname = "Doe",
            Sex = "M",
            DateOfBirth = "02/02/2010",
            Age = 16,
            Cypmd_Id = "CYPMD456",
            Identifier = "456456"
        };
        var page = new JourneyPage
        {
            Id = "reason",
            Questions = [new Question { Id = "q1", Type = QuestionType.Radio, Title = "Why?" }]
        };
        Setup(journey, page);

        var result = await _sut.GetAsync(WindowId, Reference);

        Assert.NotNull(result);
        Assert.Equal("Jane Smith 1 January 2010 (CYPMD123)", result!.FirstRecordDisplay);
        Assert.Equal("John Doe 2 February 2010 (CYPMD456)", result.SecondRecordDisplay);
    }

    private void Setup(RequestState journey, JourneyPage page)
    {
        _blob.GetAsync(WindowId, Reference).Returns(journey);
        _flow.GetConfigAsync(Arg.Any<WhatToChange>(), Arg.Any<CheckingWindowType>()).Returns(Config(page));
        _flow.GetPage(Arg.Any<QuestionFlowConfig>(), page.Id).Returns(page);
        // Default to an owning row so the org-scoping gate passes; individual tests
        // override via StubRow when they assert on specific row metadata.
        StubRow(RequestStatus.Submitted);
    }

    private static QuestionFlowConfig Config(JourneyPage page) =>
        new() { FirstPageId = page.Id, Pages = [page] };

    private static RequestState Journey(List<string>? history = null) => new()
    {
        SelectedWhatToChange = WhatToChange.Remove,
        CheckingWindow = new CheckingWindowDto
        {
            Id = WindowId,
            Title = "KS4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.UtcNow.AddDays(-10),
            EndDate = DateTime.UtcNow.AddDays(20)
        },
        ReferenceNumber = Reference,
        QuestionHistory = history ?? ["reason"],
        SelectedPupil = new PupilDto
        {
            Id = Guid.NewGuid(),
            Firstname = "Jane",
            Surname = "Smith",
            Sex = "F",
            DateOfBirth = "01/01/2010",
            Age = 16,
            Cypmd_Id = "CYPMD123",
            Identifier = "123123"
        }
    };

    private void StubRow(RequestStatus status, string? email = null, DateTime? submitted = null) =>
        _requestRepo.GetAmendmentRequestAsync(WindowId, 100000L, Reference).Returns(new AmendmentRequestData
        {
            PupilFirstname = "Jane",
            PupilSurname = "Smith",
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove",
            Status = status,
            ReferenceNumber = Reference,
            SubmittedByEmail = email,
            Submitted = submitted
        });
}
