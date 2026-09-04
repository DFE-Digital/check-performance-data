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
using Microsoft.Extensions.Caching.Memory;
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

    private static QuestionFlowConfig LoadAddKs4JuneConfig() => LoadFlowConfig("Add_KS4June.json");

    private static QuestionFlowConfig LoadFlowConfig(string fileName) =>
        JsonSerializer.Deserialize<QuestionFlowConfig>(
            File.ReadAllText(LocateFlowFile(fileName)), FlowJsonOptions)!;

    /// <summary>
    /// Serves the shipped flow files by the same <c>{WhatToChange}_{CheckingWindowType}</c> key the
    /// real blob client uses, so the whole <see cref="QuestionFlowService"/> — config lookup,
    /// page lookup, request-type resolution — runs for real against the configs that ship.
    /// </summary>
    private sealed class ShippedFlowFileClient(QuestionFlowConfig? addOverride) : IQuestionFlowConfigSource
    {
        public Task<QuestionFlowConfig?> GetConfigAsync(WhatToChange whatToChange, CheckingWindowType windowType)
        {
            if (whatToChange == WhatToChange.Add && addOverride is not null)
                return Task.FromResult<QuestionFlowConfig?>(addOverride);

            var fileName = $"{whatToChange}_{windowType}.json";
            return Task.FromResult(File.Exists(TryLocateFlowFile(fileName))
                ? LoadFlowConfig(fileName)
                : null);
        }
    }

    private static IQuestionFlowService BuildFlowService(QuestionFlowConfig? addOverride = null) =>
        new QuestionFlowService(
            new ShippedFlowFileClient(addOverride),
            new MemoryCache(new MemoryCacheOptions()));

    private static string LocateFlowFile(string fileName) =>
        TryLocateFlowFile(fileName) is { } path && File.Exists(path)
            ? path
            : throw new FileNotFoundException($"Could not locate {fileName} from " + AppContext.BaseDirectory);

    // Returns the path a flow file would occupy, whether or not it exists — the flow client has to
    // be able to answer "no such flow" (e.g. Add_Post16) without throwing.
    private static string TryLocateFlowFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return fileName;
    }

    private (RequestService Service, InMemoryRequestStateBlobClient Blob, IQuestionFlowService Flows) BuildService(
        QuestionFlowConfig addConfig)
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(UserId.ToString());
        currentUser.OrganisationUrn.Returns("142313");
        currentUser.OrganisationLaestab.Returns("860/4070");
        currentUser.DisplayName.Returns("Ada Editor");
        currentUser.Email.Returns("ada@school.test");

        var blob = new InMemoryRequestStateBlobClient();
        var flowService = BuildFlowService(addConfig);

        var service = new RequestService(
            flowService,
            blob,
            new RequestRepository(_fixture.CreateContext()),
            currentUser,
            NullLogger<RequestService>.Instance,
            new PostgresQueueService(_fixture.CreateContext()),
            Substitute.For<IRequestNotificationService>(),
            Substitute.For<ICheckYourPupilDataService>());

        return (service, blob, flowService);
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
        var (service, _, _) = BuildService(LoadAddKs4JuneConfig());
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
        var (service, _, _) = BuildService(LoadAddKs4JuneConfig());

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

    // The Add row must be skipped BECAUSE it is an Add — not incidentally, because some
    // collaborator returned nothing. So the replay runs the real QuestionFlowService over the
    // shipped configs, and a Remove row sits in the same batch: if the guard is removed the Add
    // row is replayed and committed just like its sibling, and this fails. The sibling also
    // proves the guard `continue`s rather than short-circuiting the loop.
    [Fact]
    public async Task WindowCloseReplay_CommitsTheRemoveRow_AndLeavesTheAddRowUncommitted()
    {
        await TruncateAsync();
        var windowId = await SeedKs4JuneWindowAsync();
        var (service, blob, _) = BuildService(LoadAddKs4JuneConfig());

        await service.SubmitRequestAsync(windowId, AddJourney(windowId, "CYPMD_KS4June_ADD0004"));
        await service.SubmitRequestAsync(windowId, RemoveJourney(windowId, "CYPMD_KS4June_RMV0004"));

        // Baseline: the Remove submission enqueued to the rules engine, the Add one did not.
        await using (var seeded = _fixture.CreateContext())
        {
            Assert.Equal(1, await seeded.QueueMessages.CountAsync(m => m.QueueName == QueueOptions.RulesEngineQueue));
            Assert.Equal(0, await seeded.QueueMessages.CountAsync(m => m.QueueName == QueueOptions.ZendeskQueue));
        }

        var adminService = new AdminRequestsService(
            new UncommittedRequestsRepository(_fixture.CreateContext()),
            blob,
            BuildFlowService(LoadAddKs4JuneConfig()),
            new PostgresQueueService(_fixture.CreateContext()),
            TimeProvider.System);

        var replayedCount = await adminService.ProcessCloseWindowEvent(CancellationToken.None);

        Assert.Equal(1, replayedCount);

        await using var ctx = _fixture.CreateContext();
        var addRow = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "CYPMD_KS4June_ADD0004");
        var removeRow = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "CYPMD_KS4June_RMV0004");

        Assert.Equal(RequestStatus.SubmittedUnCommitted, addRow.Status);
        Assert.Equal(RequestStatus.SubmittedCommitted, removeRow.Status);

        // Exactly one Zendesk document, and it is the Remove one — the Add reference appears on
        // no queue at all.
        var zendesk = await ctx.QueueMessages
            .Where(m => m.QueueName == QueueOptions.ZendeskQueue)
            .Select(m => m.Payload)
            .ToListAsync();
        Assert.Single(zendesk);
        Assert.Contains("CYPMD_KS4June_RMV0004", zendesk[0]);

        var allPayloads = await ctx.QueueMessages.Select(m => m.Payload).ToListAsync();
        Assert.DoesNotContain(allPayloads, p => p.Contains("CYPMD_KS4June_ADD0004"));
    }

    // A roll pupil going through the Remove journey — the ordinary amendment the replay is built
    // for, and the control the Add row is measured against.
    private static RequestState RemoveJourney(Guid windowId, string reference) => new()
    {
        SelectedWhatToChange = WhatToChange.Remove,
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
        SelectedPupil = new PupilDto
        {
            Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
            Firstname = "Ian",
            Surname = "Rollpupil",
            Sex = "M",
            DateOfBirth = "02/02/2010",
            Age = 15,
            Cypmd_Id = "CYPMD-1",
            Identifier = "A86040700009B"
        },
        QuestionAnswers = new Dictionary<string, QuestionAnswer>
        {
            ["reason"] = new() { TextValue = "permanent-exclusion" },
            ["date-pupil-excluded"] = new() { DateValue = new DateAnswer { Day = 1, Month = 3, Year = 2026 } }
        },
        QuestionHistory = ["select-pupil", "reason", "permanent-exclusion"]
    };

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
