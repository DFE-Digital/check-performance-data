using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// AB#307773: the sweep is split by school. The exercise's own sweep leaves out the schools in an
// extension still held back, and an extension's sweep takes only its own. These run the filter
// against Postgres because it rests on SQL REPLACE: the request stores the DfE Sign-In claim with
// its slash (933/4290), the extension stores digits (9334290), and both forms must match. A null
// LAESTAB is never extended, so it always goes with the exercise.
[Collection(nameof(PostgresCollection))]
public sealed class AdminRequestsRepositorySchoolFilterTests(PostgresFixture fixture)
{
    private static readonly Guid WindowId = Guid.Parse("5C400001-0000-0000-0000-000000000001");
    private static readonly Guid ExerciseId = Guid.Parse("5C400001-0000-0000-0000-000000000002");

    private AdminRequestsRepository Repository() => new(fixture.CreateContext());

    private static CheckingWindow Window() => new()
    {
        Id = WindowId,
        Title = "School filter window",
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2026, 10, 1),
        EndDate = new DateTime(2027, 3, 31),
        CheckingExercises =
        {
            new CheckingExercise
            {
                Id = ExerciseId,
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = new DateTime(2026, 10, 1),
                EndDate = new DateTime(2027, 3, 31),
                TabOrder = 0
            }
        }
    };

    private static ChangeRequest Request(
        string reference, string? laestab, RequestStatus status, ProcessingStatus? processingStatus = null) => new()
    {
        Id = Guid.NewGuid(),
        WindowId = WindowId,
        CheckingExerciseId = ExerciseId,
        OrganisationUrn = 142313,
        OrganisationLaestab = laestab,
        Submitted = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Unspecified),
        SubmittedById = Guid.Parse("99999999-9999-9999-9999-999999999999"),
        SubmittedByName = "Ada Editor",
        Status = status,
        ReferenceNumber = reference,
        RequestType = RequestType.Amendment,
        RequestTypeDescription = "Remove",
        AmendmentType = WhatToChange.Remove,
        ProcessingStatus = processingStatus
    };

    private async Task SeedAsync()
    {
        await using var ctx = fixture.CreateContext();

        await ctx.ChangeRequests.ExecuteDeleteAsync();
        await ctx.CheckingWindows.Where(w => w.Id == WindowId).ExecuteDeleteAsync();

        ctx.CheckingWindows.Add(Window());
        ctx.ChangeRequests.AddRange(
            Request("R1", "933/4290", RequestStatus.Submitted, ProcessingStatus.Decided),
            Request("R2", "9334290", RequestStatus.Submitted, ProcessingStatus.Decided),
            Request("R3", "860/4070", RequestStatus.Submitted, ProcessingStatus.Decided),
            Request("R4", null, RequestStatus.Submitted, ProcessingStatus.Decided),
            Request("D1", "933/4290", RequestStatus.InProgress),
            Request("D2", null, RequestStatus.ReadyToSubmit));

        await ctx.SaveChangesAsync();
    }

    private async Task<string[]> DecidedAsync(SchoolFilter schools) =>
        (await Repository().GetDecidedRequestsForExerciseAsync(WindowId, ExerciseId, schools, CancellationToken.None))
            .Select(r => r.ReferenceNumber)
            .Order()
            .ToArray();

    [Fact]
    public async Task Exclude_leaves_out_the_named_schools_in_either_form_and_keeps_null()
    {
        await SeedAsync();
        var schools = SchoolFilter.Exclude(["9334290"]);

        Assert.Equal(["R3", "R4"], await DecidedAsync(schools));
        Assert.Equal(1, await Repository().CountDraftsForExerciseAsync(WindowId, ExerciseId, schools, CancellationToken.None));
    }

    [Fact]
    public async Task Only_takes_just_the_named_schools_and_never_null()
    {
        await SeedAsync();
        var schools = SchoolFilter.Only(["9334290"]);

        Assert.Equal(["R1", "R2"], await DecidedAsync(schools));
        Assert.Equal(1, await Repository().CountDraftsForExerciseAsync(WindowId, ExerciseId, schools, CancellationToken.None));

        var changed = await Repository().MarkDraftsNotSubmittedForExerciseAsync(
            WindowId, ExerciseId, schools, CancellationToken.None);

        Assert.Equal(1, changed);
        await using var verify = fixture.CreateContext();
        var statuses = await verify.ChangeRequests.AsNoTracking().ToDictionaryAsync(r => r.ReferenceNumber, r => r.Status);
        Assert.Equal(RequestStatus.NotSubmitted, statuses["D1"]);
        Assert.Equal(RequestStatus.ReadyToSubmit, statuses["D2"]);
    }

    [Fact]
    public async Task None_is_todays_behaviour()
    {
        await SeedAsync();

        Assert.Equal(["R1", "R2", "R3", "R4"], await DecidedAsync(SchoolFilter.None));
        Assert.Equal(2, await Repository().CountDraftsForExerciseAsync(WindowId, ExerciseId, SchoolFilter.None, CancellationToken.None));
    }
}
