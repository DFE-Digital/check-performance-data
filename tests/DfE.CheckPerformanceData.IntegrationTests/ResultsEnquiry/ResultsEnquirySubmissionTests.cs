using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using CheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseService;

namespace DfE.CheckPerformanceData.IntegrationTests.ResultsEnquiry;

// AB#296648: submitting a results enquiry against a real Postgres.
//
// The unit tests pin what the service ASKS the repository to do; these pin what actually lands in the
// database — in particular that RequestType and AmendmentType round-trip as their readable enum names
// through the HasConversion<string>() columns. "ResultsEnquiry" (14) and "IncorrectGrade" (14) fit the
// 20-character columns, which is why this ticket needed no migration; a regression there would be a
// truncation error at submit time and nowhere earlier.
[Collection(nameof(PostgresCollection))]
public sealed class ResultsEnquirySubmissionTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid PupilId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private sealed class InMemoryRequestStateBlobClient : IRequestStateBlobClient
    {
        public Dictionary<(Guid, string), RequestState> Saved { get; } = [];

        public Task SaveAsync(Guid windowId, string referenceNumber, RequestState state)
        {
            // Round-tripped through JSON so the test sees exactly what a real blob would return —
            // a property the journey state cannot serialise would otherwise go unnoticed.
            var json = System.Text.Json.JsonSerializer.Serialize(state);
            Saved[(windowId, referenceNumber)] =
                System.Text.Json.JsonSerializer.Deserialize<RequestState>(json)!;
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

    private (RequestService Service, InMemoryRequestStateBlobClient Blob, IQueueService Queue) BuildService()
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(UserId.ToString());
        currentUser.OrganisationUrn.Returns("142313");
        currentUser.OrganisationLaestab.Returns("860/4070");
        currentUser.DisplayName.Returns("Ada Editor");
        currentUser.Email.Returns("ada@school.test");

        var blob = new InMemoryRequestStateBlobClient();
        var queue = Substitute.For<IQueueService>();

        var service = new RequestService(
            Substitute.For<IQuestionFlowService>(),
            blob,
            new RequestRepository(_fixture.CreateContext()),
            currentUser,
            NullLogger<RequestService>.Instance,
            queue,
            Substitute.For<IRequestNotificationService>(),
            Substitute.For<ICheckYourPupilDataService>(),
            new CheckingExerciseService(TimeProvider.System));

        return (service, blob, queue);
    }

    private static RequestState Journey(Guid windowId, string reference, bool cohortWide = true) => new()
    {
        SelectedWhatToChange = WhatToChange.IncorrectGrade,
        CheckingWindow = new CheckingWindowDto
        {
            Title = "16 to 19 2026", KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2027, 3, 31)
        },
        ReferenceNumber = reference,
        SelectedPupilId = PupilId.ToString(),
        SelectedPupil = new PupilDto
        {
            Id = PupilId, Firstname = "Billy", Surname = "B", Sex = "M",
            DateOfBirth = "12/03/2007", Age = 19, Cypmd_Id = "1596410810", Identifier = "9900000001"
        },
        SelectedResult = new StudentResultRecord
        {
            CypmdId = "1596410810", Qan = "60180882",
            QualificationName = "GCSE (9-1) Art&Des : Fine Art", SyllabusCode = "1AD0",
            Session = "S2024", Grade = "9", SourceFile = ResultsFileTags.Post16Main
        },
        QuestionAnswers = new Dictionary<string, QuestionAnswer>
        {
            ["q-cohort-scope"] = new() { TextValue = cohortWide ? "yes" : "no" },
            ["q-cohort-count"] = new() { TextValue = cohortWide ? "10" : null },
            ["q-revised-grade"] = new() { TextValue = "U" },
            ["q-additional-info"] = new() { TextValue = "Marked against the wrong paper." }
        },
        QuestionHistory = ["cohort-scope", "cohort-count", "select-student-cohort", "select-result", "grade-details", "additional-info"]
    };

    [Fact]
    public async Task An_enquiry_is_persisted_as_a_results_enquiry_row()
    {
        await TruncateAsync();
        var windowId = await SeedPost16WindowAsync();
        var (service, _, _) = BuildService();

        var reference = await service.SubmitResultsEnquiryAsync(windowId, Journey(windowId, "CYPMD_16to19_RE_AAAAAA1"));

        Assert.Equal("CYPMD_16to19_RE_AAAAAA1", reference);
        await using var ctx = _fixture.CreateContext();
        var row = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == reference);
        Assert.Equal(RequestType.ResultsEnquiry, row.RequestType);
        Assert.Equal(WhatToChange.IncorrectGrade, row.AmendmentType);
        Assert.Equal(RequestStatus.SubmittedUnCommitted, row.Status);
        Assert.Equal("Results enquiry - Incorrect grade", row.RequestTypeDescription);
        Assert.Equal(PupilId, row.PupilId);
        Assert.Equal("Billy", row.PupilFirstname);
        Assert.Equal(142313L, row.OrganisationUrn);
    }

    [Fact]
    public async Task The_enum_columns_hold_readable_names_not_ordinals()
    {
        // Both columns are HasConversion<string>() and capped at 20 characters. This is the assertion
        // that proves no migration was needed.
        await TruncateAsync();
        var windowId = await SeedPost16WindowAsync();
        var (service, _, _) = BuildService();

        await service.SubmitResultsEnquiryAsync(windowId, Journey(windowId, "CYPMD_16to19_RE_BBBBBB2"));

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            @"SELECT ""RequestType"", ""AmendmentType"" FROM ""ChangeRequests""
              WHERE ""ReferenceNumber"" = 'CYPMD_16to19_RE_BBBBBB2';";
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("ResultsEnquiry", reader.GetString(0));
        Assert.Equal("IncorrectGrade", reader.GetString(1));
    }

    [Fact]
    public async Task The_journey_json_round_trips_with_the_selected_result_and_answers()
    {
        await TruncateAsync();
        var windowId = await SeedPost16WindowAsync();
        var (service, blob, _) = BuildService();

        await service.SubmitResultsEnquiryAsync(windowId, Journey(windowId, "CYPMD_16to19_RE_CCCCCC3"));

        var saved = await blob.GetAsync(windowId, "CYPMD_16to19_RE_CCCCCC3");
        Assert.NotNull(saved);
        Assert.Equal(WhatToChange.IncorrectGrade, saved.SelectedWhatToChange);
        Assert.NotNull(saved.SelectedResult);
        Assert.Equal("60180882", saved.SelectedResult.Qan);
        Assert.Equal("9", saved.SelectedResult.Grade);
        Assert.Equal("S2024", saved.SelectedResult.Session);
        Assert.Equal("GCSE (9-1) Art&Des : Fine Art", saved.SelectedResult.QualificationName);
        Assert.Equal("U", saved.QuestionAnswers["q-revised-grade"].TextValue);
        Assert.Equal("10", saved.QuestionAnswers["q-cohort-count"].TextValue);
        Assert.Equal("Marked against the wrong paper.", saved.QuestionAnswers["q-additional-info"].TextValue);
    }

    [Fact]
    public async Task Nothing_is_enqueued()
    {
        // BA decision 2026-08-17: Zendesk dispatch is a separate story.
        await TruncateAsync();
        var windowId = await SeedPost16WindowAsync();
        var (service, _, queue) = BuildService();

        await service.SubmitResultsEnquiryAsync(windowId, Journey(windowId, "CYPMD_16to19_RE_DDDDDD4"));

        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync<object>(default!, default!);
    }

    [Fact]
    public async Task A_second_enquiry_for_the_same_pupil_and_result_also_persists()
    {
        // "The spec allows multiple enquiries per pupil/result; the docs never restrict."
        await TruncateAsync();
        var windowId = await SeedPost16WindowAsync();
        var (service, _, _) = BuildService();

        await service.SubmitResultsEnquiryAsync(windowId, Journey(windowId, "CYPMD_16to19_RE_EEEEEE5"));
        await service.SubmitResultsEnquiryAsync(windowId, Journey(windowId, "CYPMD_16to19_RE_FFFFFF6"));

        await using var ctx = _fixture.CreateContext();
        var rows = await ctx.ChangeRequests
            .Where(r => r.RequestType == RequestType.ResultsEnquiry)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.ReferenceNumber).Distinct().Count());
        Assert.All(rows, r => Assert.Equal(PupilId, r.PupilId));
    }

    [Fact]
    public async Task An_enquiry_does_not_block_a_pupil_data_amendment_for_the_same_pupil()
    {
        // The duplicate rule exists to stop two competing amendments to one pupil's record. An
        // enquiry does not change pupil data, so it must not consume that slot — otherwise a school
        // that reports a wrong grade for Billy can no longer ask to remove Billy, and gets a
        // duplicate-request error naming an enquiry that has nothing to do with it.
        await TruncateAsync();
        var windowId = await SeedPost16WindowAsync();
        var (service, _, _) = BuildService();
        await service.SubmitResultsEnquiryAsync(windowId, Journey(windowId, "CYPMD_16to19_RE_9999999"));

        await new RequestRepository(_fixture.CreateContext()).UpsertAsync(new ChangeRequestData
        {
            WindowId = windowId,
            ReferenceNumber = "CYPMD_Post16_AMEND01",
            OrganisationUrn = 142313,
            PupilId = PupilId,
            PupilFirstname = "Billy",
            PupilSurname = "B",
            Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            SubmittedById = UserId,
            SubmittedByName = "Ada Editor",
            Status = RequestStatus.SubmittedUnCommitted,
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove - not-on-roll",
            AmendmentType = WhatToChange.Remove
        });

        await using var ctx = _fixture.CreateContext();
        Assert.Equal(2, await ctx.ChangeRequests.CountAsync(r => r.PupilId == PupilId));
    }

    [Fact]
    public async Task An_existing_amendment_does_not_block_an_enquiry_for_the_same_pupil()
    {
        // The other direction. A school mid-way through a removal request must still be able to
        // report a wrong grade for the same student.
        await TruncateAsync();
        var windowId = await SeedPost16WindowAsync();
        await new RequestRepository(_fixture.CreateContext()).UpsertAsync(new ChangeRequestData
        {
            WindowId = windowId,
            ReferenceNumber = "CYPMD_Post16_AMEND02",
            OrganisationUrn = 142313,
            PupilId = PupilId,
            PupilFirstname = "Billy",
            PupilSurname = "B",
            Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            SubmittedById = UserId,
            SubmittedByName = "Ada Editor",
            Status = RequestStatus.SubmittedUnCommitted,
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove - not-on-roll",
            AmendmentType = WhatToChange.Remove
        });
        var (service, _, _) = BuildService();

        await service.SubmitResultsEnquiryAsync(windowId, Journey(windowId, "CYPMD_16to19_RE_ABCDEF1"));

        await using var ctx = _fixture.CreateContext();
        Assert.Equal(2, await ctx.ChangeRequests.CountAsync(r => r.PupilId == PupilId));
    }

    [Fact]
    public async Task Two_competing_amendments_for_one_pupil_are_still_blocked()
    {
        // The rule the exclusion must not weaken.
        await TruncateAsync();
        var windowId = await SeedPost16WindowAsync();

        ChangeRequestData Amendment(string reference) => new()
        {
            WindowId = windowId,
            ReferenceNumber = reference,
            OrganisationUrn = 142313,
            PupilId = PupilId,
            PupilFirstname = "Billy",
            PupilSurname = "B",
            Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            SubmittedById = UserId,
            SubmittedByName = "Ada Editor",
            Status = RequestStatus.SubmittedUnCommitted,
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove - not-on-roll",
            AmendmentType = WhatToChange.Remove
        };

        await new RequestRepository(_fixture.CreateContext()).UpsertAsync(Amendment("CYPMD_Post16_AMEND03"));

        await Assert.ThrowsAsync<DuplicateRequestException>(() =>
            new RequestRepository(_fixture.CreateContext()).UpsertAsync(Amendment("CYPMD_Post16_AMEND04")));
    }

    [Fact]
    public async Task The_single_pupil_branch_persists_without_a_cohort_count()
    {
        await TruncateAsync();
        var windowId = await SeedPost16WindowAsync();
        var (service, blob, _) = BuildService();

        await service.SubmitResultsEnquiryAsync(
            windowId, Journey(windowId, "CYPMD_16to19_RE_7777777", cohortWide: false));

        var saved = await blob.GetAsync(windowId, "CYPMD_16to19_RE_7777777");
        Assert.NotNull(saved);
        Assert.Equal("no", saved.QuestionAnswers["q-cohort-scope"].TextValue);
        Assert.Null(saved.QuestionAnswers["q-cohort-count"].TextValue);
    }

    [Fact]
    public async Task The_submitted_timestamp_is_stored()
    {
        await TruncateAsync();
        var windowId = await SeedPost16WindowAsync();
        var (service, _, _) = BuildService();
        var before = DateTime.UtcNow.AddSeconds(-5);

        await service.SubmitResultsEnquiryAsync(windowId, Journey(windowId, "CYPMD_16to19_RE_8888888"));

        await using var ctx = _fixture.CreateContext();
        var row = await ctx.ChangeRequests.SingleAsync(r => r.ReferenceNumber == "CYPMD_16to19_RE_8888888");
        Assert.True(row.Submitted >= before, $"Submitted {row.Submitted} should be at or after {before}.");
    }

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ChangeRequests"" CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedPost16WindowAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            Title = "16 to 19 2026",
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified),
            EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified)
        };
        ctx.CheckingWindows.Add(window);
        await ctx.SaveChangesAsync();
        return window.Id;
    }
}
