using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.AdminRequests;
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
using CloseExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.CloseExerciseService;
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;
using CheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseService;
using IWindowService = DfE.CheckPerformanceData.Application.WindowManagement.IWindowService;

namespace DfE.CheckPerformanceData.IntegrationTests.RequestSubmission;

// AB#297310: submitting an Add-a-pupil request against a real Postgres.
//
// The unit tests (RequestServiceAddTests) pin what the service ASKS its collaborators to do;
// these pin what actually lands in the database — in particular that the row persists and (#536)
// one message lands on the real rules-engine queue table, as for every other amendment. Mirrors
// ResultsEnquirySubmissionTests' fixture wiring.
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

    // Keeps the RequestDocument saved at submit so the close sweep can send it (#536).
    private sealed class InMemoryRequestBlobClient : IRequestBlobClient
    {
        private readonly Dictionary<(Guid, string), RequestDocument> _saved = [];

        public Task SaveRequestAsync(Guid windowId, RequestDocument document)
        {
            _saved[(windowId, document.ReferenceNumber)] = document;
            return Task.CompletedTask;
        }

        public Task<RequestDocument?> GetRequestAsync(Guid windowId, string referenceNumber) =>
            Task.FromResult(_saved.TryGetValue((windowId, referenceNumber), out var d) ? d : null);
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
        public bool Exists(WhatToChange whatToChange, CheckingWindowType checkingWindowType) =>
            GetConfigAsync(whatToChange, checkingWindowType).GetAwaiter().GetResult() is not null;

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

    private (RequestService Service, InMemoryRequestBlobClient Documents) BuildService(
        QuestionFlowConfig addConfig)
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(UserId.ToString());
        currentUser.OrganisationUrn.Returns("142313");
        currentUser.OrganisationLaestab.Returns("860/4070");
        currentUser.DisplayName.Returns("Ada Editor");
        currentUser.Email.Returns("ada@school.test");

        var documents = new InMemoryRequestBlobClient();

        var service = new RequestService(
            BuildFlowService(addConfig),
            new InMemoryRequestStateBlobClient(),
            documents,
            new RequestRepository(_fixture.CreateContext()),
            currentUser,
            NullLogger<RequestService>.Instance,
            new PostgresQueueService(_fixture.CreateContext()),
            Substitute.For<IRequestNotificationService>(),
            Substitute.For<ICheckYourPupilDataService>(),
            new CheckingExerciseService(TimeProvider.System));

        return (service, documents);
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
                EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified),
                Exercises = [PupilDataExercise(windowId)]
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
    public async Task SubmitAdd_WritesAmendmentRow_WithAddType_AndOneRulesEngineMessage()
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
        Assert.Equal(RequestStatus.Submitted, row.Status);
        Assert.Equal(journey.SelectedPupil!.Id, row.PupilId);
        Assert.Equal("Alice", row.PupilFirstname);
        Assert.Equal("Newpupil", row.PupilSurname);

        // #536: Add goes through the Rules Engine like every other amendment (it has a single
        // Scrutiny rule until Add rules are written).
        var queueCount = await ctx.QueueMessages.CountAsync(m => m.QueueName == QueueOptions.RulesEngineQueue);
        Assert.Equal(1, queueCount);
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

    // Closing the pupil-data exercise sends a decided Add exactly like the decided Remove beside it
    // (#536). Neither is sent before the Rules Engine decides it: the sweep reports both as
    // waiting first. The Remove row is the control: it proves the sweep treats every decided
    // amendment the same, Add included.
    [Fact]
    public async Task ClosingPupilData_SendsBothTheAddAndTheRemoveRow_OnceDecided()
    {
        await TruncateAsync();
        var windowId = await SeedKs4JuneWindowAsync();
        var (service, documents) = BuildService(LoadAddKs4JuneConfig());

        await service.SubmitRequestAsync(windowId, AddJourney(windowId, "CYPMD_KS4June_ADD0004"));
        await service.SubmitRequestAsync(windowId, RemoveJourney(windowId, "CYPMD_KS4June_RMV0004"));

        // Baseline: both submissions enqueued to the rules engine (#536), neither to Zendesk.
        await using (var seeded = _fixture.CreateContext())
        {
            Assert.Equal(2, await seeded.QueueMessages.CountAsync(m => m.QueueName == QueueOptions.RulesEngineQueue));
            Assert.Equal(0, await seeded.QueueMessages.CountAsync(m => m.QueueName == QueueOptions.ZendeskQueue));
        }

        var closeService = new CloseExerciseService(
            new AdminRequestsRepository(_fixture.CreateContext()),
            documents,
            new PostgresQueueService(_fixture.CreateContext()));

        // Undecided: the sweep would send nothing and says both are waiting.
        var waiting = await closeService.PreviewAsync(
            windowId, PupilDataExerciseId(windowId), CancellationToken.None);
        Assert.Equal(0, waiting.RequestsToClose);
        Assert.Equal(2, waiting.RequestsWaiting);

        // The Rules Engine decides both (what RulesConsumer writes).
        await using (var decide = _fixture.CreateContext())
        {
            await decide.ChangeRequests
                .Where(r => r.ReferenceNumber == "CYPMD_KS4June_ADD0004" || r.ReferenceNumber == "CYPMD_KS4June_RMV0004")
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.ProcessingStatus, ProcessingStatus.Decided));
        }

        var preview = await closeService.PreviewAsync(
            windowId, PupilDataExerciseId(windowId), CancellationToken.None);
        Assert.Equal(2, preview.RequestsToClose);
        Assert.Equal(0, preview.RequestsWaiting);

        var result = await closeService.CloseAsync(
            windowId, PupilDataExerciseId(windowId), CancellationToken.None);

        Assert.Equal(2, result.Enqueued);
        Assert.Equal(0, result.Waiting);

        await using var ctx = _fixture.CreateContext();
        var addRow = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "CYPMD_KS4June_ADD0004");
        var removeRow = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "CYPMD_KS4June_RMV0004");

        // The sweep moves ProcessingStatus only; Status still says what the school did.
        Assert.Equal(ProcessingStatus.TicketQueued, addRow.ProcessingStatus);
        Assert.Equal(ProcessingStatus.TicketQueued, removeRow.ProcessingStatus);
        Assert.Equal(RequestStatus.Submitted, addRow.Status);
        Assert.Equal(RequestStatus.Submitted, removeRow.Status);

        // Both references reach Zendesk, one saved document each.
        var zendesk = await ctx.QueueMessages
            .Where(m => m.QueueName == QueueOptions.ZendeskQueue)
            .Select(m => m.Payload)
            .ToListAsync();
        Assert.Equal(2, zendesk.Count);
        Assert.Contains(zendesk, p => p.Contains("CYPMD_KS4June_ADD0004"));
        Assert.Contains(zendesk, p => p.Contains("CYPMD_KS4June_RMV0004"));

        // A second run finds nothing left to send.
        var again = await closeService.CloseAsync(
            windowId, PupilDataExerciseId(windowId), CancellationToken.None);
        Assert.Equal(0, again.Enqueued);
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
            EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified),
            Exercises = [PupilDataExercise(windowId)]
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

    // RequestService stamps a request's CheckingExerciseId from the JOURNEY's own Exercises list
    // (ICheckingExerciseService.IdFor), not from the database — so the seeded exercise row and the
    // session DTO in each journey below have to name the same id, or every row lands orphaned and
    // the per-exercise close sweep finds nothing.
    // Derived from the window id rather than a constant: every test here seeds its own window into
    // the same shared database, so a fixed exercise id collides on the second one.
    private static Guid PupilDataExerciseId(Guid windowId)
    {
        var bytes = windowId.ToByteArray();
        bytes[15] ^= 0x01;
        return new Guid(bytes);
    }

    private static CheckingExerciseDto PupilDataExercise(Guid windowId) => new()
    {
        Id = PupilDataExerciseId(windowId),
        ExerciseType = CheckingExerciseType.PupilData,
        StartDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified),
        EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified),
        TabOrder = 0
    };

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
        // A KS4 June window runs pupil-data checking. The exercise row has to exist for
        // RequestService to stamp CheckingExerciseId onto the submitted requests, which is what the
        // per-exercise close sweep matches on — without it every row is orphaned and closing the
        // exercise finds nothing.
        window.CheckingExercises.Add(new CheckingExercise
        {
            Id = PupilDataExerciseId(window.Id),
            ExerciseType = CheckingExerciseType.PupilData,
            StartDate = window.StartDate,
            EndDate = window.EndDate,
            TabOrder = 0
        });

        ctx.CheckingWindows.Add(window);
        await ctx.SaveChangesAsync();
        return window.Id;
    }
}
