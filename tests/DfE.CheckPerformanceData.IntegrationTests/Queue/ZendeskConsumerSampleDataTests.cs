using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.RulesEngineWorker.Consumers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Queue;

// #942: a request that a school made against a release that gave it sample data becomes a ticket
// that agents can find and leave: a tag and a subject prefix. Other requests do not change.
[Collection(nameof(PostgresCollection))]
public sealed class ZendeskConsumerSampleDataTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly Guid _windowId = Guid.NewGuid();
    private readonly Guid _releaseId = Guid.NewGuid();
    private readonly string _sampleReference = $"REF-SAMPLE-{Guid.NewGuid():N}"[..20];
    private readonly string _realReference = $"REF-REAL-{Guid.NewGuid():N}"[..20];

    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        var exerciseId = Guid.NewGuid();
        ctx.CheckingWindows.Add(new CheckingWindow
        {
            Id = _windowId, Title = "KS2", KeyStage = KeyStages.KS2, CheckingWindowType = CheckingWindowType.KS2,
            StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 10, 1),
            CheckingExercises =
            [
                new CheckingExercise
                {
                    Id = exerciseId, CheckingWindowId = _windowId, ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 10, 1), TabName = "Pupils",
                    Releases =
                    [
                        new CheckingExerciseRelease
                        {
                            Id = _releaseId, CheckingExerciseId = exerciseId, Number = 1,
                            PublishedAt = new DateTime(2026, 9, 1), SampleLaestabs = ["9334290"]
                        }
                    ]
                }
            ]
        });
        ctx.ChangeRequests.Add(Request(_sampleReference, "933/4290"));
        ctx.ChangeRequests.Add(Request(_realReference, "933/4291"));
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.ChangeRequests.Where(r => r.WindowId == _windowId).ExecuteDeleteAsync();
        await ctx.CheckingWindows.Where(w => w.Id == _windowId).ExecuteDeleteAsync();
    }

    private ChangeRequest Request(string reference, string laestab) => new()
    {
        Id = Guid.NewGuid(),
        WindowId = _windowId,
        CheckingExerciseReleaseId = _releaseId,
        OrganisationUrn = 100000,
        OrganisationLaestab = laestab,
        Submitted = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        SubmittedById = Guid.NewGuid(),
        SubmittedByName = "Test User",
        ProcessingStatus = ProcessingStatus.TicketQueued,
        Status = RequestStatus.Submitted,
        ReferenceNumber = reference,
        RequestType = RequestType.Amendment,
        RequestTypeDescription = "Not on roll",
        Outcome = DecisionStatus.Scrutiny
    };

    private static string Message(string reference) => $$"""
    {
        "ChangeRequestId": "11111111-2222-3333-4444-555555555555",
        "ReferenceNumber": "{{reference}}",
        "SubmittedAt": "2026-09-10T00:00:00Z",
        "SubmittedBy": { "UserId": "u1", "DisplayName": "Test User" },
        "CheckingWindowId": "11111111-1111-1111-1111-111111111111",
        "CheckingWindowType": "KS2",
        "RequestTypeCode": "not-on-roll",
        "School": { "Urn": "100000", "Name": "Test School" },
        "Pupil": {
            "Id": "p1", "CypmdId": "c1", "Firstname": "Ann", "Surname": "Bell",
            "DateOfBirth": "2015-01-01", "Sex": "F", "Age": 9, "Upn": "X123"
        },
        "Answers": []
    }
    """;

    private async Task<CreateTicketRequestDto> TicketForAsync(string reference)
    {
        var zendesk = Substitute.For<IZendeskService>();
        CreateTicketRequestDto? sent = null;
        zendesk.CreateTicketAsync(Arg.Do<CreateTicketRequestDto>(t => sent = t))
            .Returns(new CreateTicketResponseDto { Ticket = new TicketDto { Id = 9000 } });

        await using var ctx = fixture.CreateContext();
        var consumer = new ZendeskConsumer(Substitute.For<Application.Queue.IQueueService>(), zendesk, ctx);
        await consumer.ProcessMessageBodyAsync(Message(reference), CancellationToken.None);
        return sent!;
    }

    [Fact]
    public async Task A_request_from_a_sample_school_gets_the_tag_and_the_subject_prefix()
    {
        var ticket = await TicketForAsync(_sampleReference);

        Assert.StartsWith(ZendeskConsumer.SampleDataSubjectPrefix, ticket.Ticket.Subject);
        Assert.Equal([ZendeskConsumer.SampleDataTag], ticket.Ticket.Tags);
    }

    [Fact]
    public async Task A_request_from_another_school_of_the_same_release_is_not_marked()
    {
        var ticket = await TicketForAsync(_realReference);

        Assert.DoesNotContain("SAMPLE", ticket.Ticket.Subject);
        Assert.Null(ticket.Ticket.Tags);
    }
}
