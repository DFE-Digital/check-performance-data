using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.UncommittedRequests;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.RequestSubmission;

// AB#297310: submitting an Add-a-pupil request against a real Postgres.
//
// The unit tests (RequestServiceAddTests) pin what the service ASKS its collaborators to do;
// these pin what actually lands in the database — in particular that the row persists with zero
// rows landing on the real rules-engine queue table, and that the window-close replay leaves the
// row untouched. Mirrors ResultsEnquirySubmissionTests' fixture wiring.
[Collection(nameof(PostgresCollection))]
public sealed class AddRequestSubmissionTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private static readonly Guid UserId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static readonly JsonSerializerOptions FlowJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class InMemoryRequestStateBlobClient : IRequestStateBlobClient
    {
        public Dictionary<(Guid, string), RequestState> Saved { get; } = [];

        public Task SaveAsync(Guid windowId, string referenceNumber, RequestState state)
        {
            var json = JsonSerializer.Serialize(state);
            Saved[(windowId, referenceNumber)] = JsonSerializer.Deserialize<RequestState>(json)!;
            return Task.CompletedTask;
        }

        public Task<RequestState?> GetAsync(Guid windowId, string referenceNumber) =>
            Task.FromResult(Saved.TryGetValue((windowId, referenceNumber), out var s) ? s : null);

        public Task DeleteAsync(Guid windowId, string referenceNumber)
        {
            Saved.Remove((windowId, referenceNumber));
            return Task.CompletedTask;
        }
    }

    private static QuestionFlowConfig LoadAddKs4JuneConfig()
    {
        var path = LocateFlowFile("Add_KS4June.json");
        return JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(path), FlowJsonOptions)!;
    }

    private static string LocateFlowFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate {fileName} from " + AppContext.BaseDirectory);
    }

    private (RequestService Service, InMemoryRequestStateBlobClient Blob) BuildService(QuestionFlowConfig addConfig)
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(UserId.ToString());
        currentUser.OrganisationUrn.Returns("142313");
        currentUser.OrganisationLaestab.Returns("860/4070");
        currentUser.DisplayName.Returns("Ada Editor");
        currentUser.Email.Returns("ada@school.test");

        var blob = new InMemoryRequestStateBlobClient();
        var flowService = Substitute.For<IQuestionFlowService>();
        flowService.GetConfigAsync(WhatToChange.Add, CheckingWindowType.KS4June).Returns(addConfig);

        var service = new RequestService(
            flowService,
            blob,
            new RequestRepository(_fixture.CreateContext()),
            currentUser,
            NullLogger<RequestService>.Instance,
            new PostgresQueueService(_fixture.CreateContext()),
            Substitute.For<IRequestNotificationService>(),
            Substitute.For<ICheckYourPupilDataService>());

        return (service, blob);
    }

    // Mints the synthetic pupil the same way JourneyController.PagePost does, from a
    // learner-details answer set matching Add_KS4June.json's question contract.
    private static RequestState AddJourney(Guid windowId, string reference)
    {
        var journey = new RequestState
        {
            SelectedWhatToChange = WhatToChange.Add,
            CheckingWindow = new CheckingWindowDto
            {
                Id = windowId,
                Title = "KS4 June 2026",
                KeyStage = KeyStages.KS4,
                CheckingWindowType = CheckingWindowType.KS4June,
                StartDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified),
                EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified)
            },
            ReferenceNumber = reference,
            QuestionAnswers = new Dictionary<string, QuestionAnswer>
            {
                [AddPupilJourney.FirstNameQuestionId] = new() { TextValue = "Alice" },
                [AddPupilJourney.LastNameQuestionId] = new() { TextValue = "Newpupil" },
                [AddPupilJourney.DateOfBirthQuestionId] = new() { DateValue = new DateAnswer { Day = 1, Month = 9, Year = 2010 } },
                [AddPupilJourney.SexQuestionId] = new() { TextValue = "F" },
                [AddPupilJourney.UpnQuestionId] = new() { TextValue = "A123456789012" },
                ["admission-date"] = new() { DateValue = new DateAnswer { Day = 1, Month = 9, Year = 2025 } },
                ["year-group"] = new() { TextValue = "10" },
                ["sen-status"] = new() { TextValue = "N" }
            },
            QuestionHistory = [AddPupilJourney.LearnerDetailsPageId, AddPupilJourney.AdmissionDetailsPageId, "evidence"]
        };

        journey.SelectedPupil = AddPupilJourney.BuildPupil(journey, existingId: null);
        journey.SelectedPupilId = journey.SelectedPupil.Id.ToString();
        journey.SelectedPupilLabel = $"{journey.SelectedPupil.Surname}, {journey.SelectedPupil.Firstname}";

        return journey;
    }

    [Fact]
    public async Task SubmitAdd_WritesAmendmentRow_WithAddType_AndNoQueueMessage()
    {
        await TruncateAsync();
        var windowId = await SeedKs4JuneWindowAsync();
        var (service, _) = BuildService(LoadAddKs4JuneConfig());
        var journey = AddJourney(windowId, "CYPMD_KS4June_ADD0001");

        await service.SubmitRequestAsync(windowId, journey);

        await using var ctx = _fixture.CreateContext();
        var row = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "CYPMD_KS4June_ADD0001");
        Assert.Equal(RequestType.Amendment, row.RequestType);
        Assert.Equal(WhatToChange.Add, row.AmendmentType);
        Assert.Equal(RequestStatus.SubmittedUnCommitted, row.Status);
        Assert.Equal(journey.SelectedPupil!.Id, row.PupilId);
        Assert.Equal("Alice", row.PupilFirstname);
        Assert.Equal("Newpupil", row.PupilSurname);

        var queueCount = await ctx.QueueMessages.CountAsync(m => m.QueueName == QueueOptions.RulesEngineQueue);
        Assert.Equal(0, queueCount);
    }

    [Fact]
    public async Task SubmitAdd_ThenSubmitAdd_ForADifferentTypedPupil_DoesNotConflict()
    {
        await TruncateAsync();
        var windowId = await SeedKs4JuneWindowAsync();
        var (service, _) = BuildService(LoadAddKs4JuneConfig());

        var first = AddJourney(windowId, "CYPMD_KS4June_ADD0002");
        var second = AddJourney(windowId, "CYPMD_KS4June_ADD0003");
        second.QuestionAnswers[AddPupilJourney.FirstNameQuestionId] = new QuestionAnswer { TextValue = "Bob" };
        second.QuestionAnswers[AddPupilJourney.LastNameQuestionId] = new QuestionAnswer { TextValue = "Othertypedpupil" };
        second.SelectedPupil = AddPupilJourney.BuildPupil(second, existingId: null);
        second.SelectedPupilId = second.SelectedPupil.Id.ToString();

        await service.SubmitRequestAsync(windowId, first);
        await service.SubmitRequestAsync(windowId, second);

        await using var ctx = _fixture.CreateContext();
        var rows = await ctx.ChangeRequests
            .Where(r => r.AmendmentType == WhatToChange.Add)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.PupilId).Distinct().Count());
    }

    [Fact]
    public async Task WindowCloseReplay_LeavesAddRowUncommitted()
    {
        await TruncateAsync();
        var windowId = await SeedKs4JuneWindowAsync();
        var (service, blob) = BuildService(LoadAddKs4JuneConfig());
        var journey = AddJourney(windowId, "CYPMD_KS4June_ADD0004");

        await service.SubmitRequestAsync(windowId, journey);

        var adminService = new AdminRequestsService(
            new UncommittedRequestsRepository(_fixture.CreateContext()),
            blob,
            Substitute.For<IQuestionFlowService>(),
            new PostgresQueueService(_fixture.CreateContext()),
            TimeProvider.System);

        var replayedCount = await adminService.ProcessCloseWindowEvent(CancellationToken.None);

        Assert.Equal(0, replayedCount);
        await using var ctx = _fixture.CreateContext();
        var row = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "CYPMD_KS4June_ADD0004");
        Assert.Equal(RequestStatus.SubmittedUnCommitted, row.Status);

        var queueCount = await ctx.QueueMessages.CountAsync();
        Assert.Equal(0, queueCount);
    }

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ChangeRequests"" CASCADE;";
        await cmd.ExecuteNonQueryAsync();
        try
        {
            await using var queueCmd = conn.CreateCommand();
            queueCmd.CommandText = @"TRUNCATE TABLE queue_messages RESTART IDENTITY CASCADE;";
            await queueCmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException)
        {
            // Table not present yet (migration not applied) — nothing to truncate.
        }
    }

    private async Task<Guid> SeedKs4JuneWindowAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            Title = "KS4 June 2026",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified),
            EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified)
        };
        ctx.CheckingWindows.Add(window);
        await ctx.SaveChangesAsync();
        return window.Id;
    }
}
