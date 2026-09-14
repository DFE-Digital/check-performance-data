using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// The close sweep is scoped to ONE window AND ONE exercise. These pin the three ways a row can fall
// outside that scope, because the scoping is the whole reason the close moved off the Amendment
// Requests page: the sweep it replaced took every open window at once.
//
// A row with a NULL CheckingExerciseId is deliberately left alone rather than swept under a guessed
// exercise. The FK is onDelete: SetNull, so such a row was orphaned by deleting an exercise, or
// belongs to a window that never ran the mapped one. It stays SubmittedUnCommitted and visible on
// the Requests page — a data problem stays visible.
[Collection(nameof(PostgresCollection))]
public sealed class AdminRequestsRepositoryExerciseScopeTests(PostgresFixture fixture)
{
    private static readonly Guid TargetWindowId = Guid.Parse("A1111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherWindowId = Guid.Parse("B2222222-2222-2222-2222-222222222222");
    private static readonly Guid TargetExerciseId = Guid.Parse("C3333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherExerciseId = Guid.Parse("D4444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherWindowExerciseId = Guid.Parse("E5555555-5555-5555-5555-555555555555");

    private AdminRequestsRepository Repository() => new(fixture.CreateContext());

    private static CheckingWindow Window(Guid id, string title, params (Guid Id, CheckingExerciseType Type)[] exercises)
    {
        var window = new CheckingWindow
        {
            Id = id,
            Title = title,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = new DateTime(2026, 10, 1),
            EndDate = new DateTime(2027, 3, 31)
        };

        var order = 0;
        foreach (var (exerciseId, type) in exercises)
        {
            window.CheckingExercises.Add(new CheckingExercise
            {
                Id = exerciseId,
                ExerciseType = type,
                StartDate = window.StartDate,
                EndDate = window.EndDate,
                SortOrder = order++
            });
        }

        return window;
    }

    private static ChangeRequest Request(
        Guid windowId, Guid? exerciseId, string reference, RequestStatus status) => new()
    {
        Id = Guid.NewGuid(),
        WindowId = windowId,
        CheckingExerciseId = exerciseId,
        OrganisationUrn = 142313,
        Submitted = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Unspecified),
        SubmittedById = Guid.Parse("99999999-9999-9999-9999-999999999999"),
        SubmittedByName = "Ada Editor",
        Status = status,
        ReferenceNumber = reference,
        RequestType = RequestType.Amendment,
        RequestTypeDescription = "Remove",
        AmendmentType = WhatToChange.Remove
    };

    private async Task SeedAsync()
    {
        await using var ctx = fixture.CreateContext();

        await ctx.ChangeRequests.ExecuteDeleteAsync();
        await ctx.CheckingWindows
            .Where(w => w.Id == TargetWindowId || w.Id == OtherWindowId)
            .ExecuteDeleteAsync();

        ctx.CheckingWindows.Add(Window(TargetWindowId, "Target window",
            (TargetExerciseId, CheckingExerciseType.PupilData),
            (OtherExerciseId, CheckingExerciseType.ResultsEnquiry)));
        ctx.CheckingWindows.Add(Window(OtherWindowId, "Other window",
            (OtherWindowExerciseId, CheckingExerciseType.PupilData)));

        ctx.ChangeRequests.AddRange(
            // In scope.
            Request(TargetWindowId, TargetExerciseId, "IN_SCOPE_SUBMITTED", RequestStatus.SubmittedUnCommitted),
            Request(TargetWindowId, TargetExerciseId, "IN_SCOPE_DRAFT", RequestStatus.InProgress),
            Request(TargetWindowId, TargetExerciseId, "IN_SCOPE_READY", RequestStatus.ReadyToSubmit),
            // Out of scope: another exercise in the same window.
            Request(TargetWindowId, OtherExerciseId, "OTHER_EXERCISE_SUBMITTED", RequestStatus.SubmittedUnCommitted),
            Request(TargetWindowId, OtherExerciseId, "OTHER_EXERCISE_DRAFT", RequestStatus.InProgress),
            // Out of scope: the same exercise type in a different window.
            Request(OtherWindowId, OtherWindowExerciseId, "OTHER_WINDOW_SUBMITTED", RequestStatus.SubmittedUnCommitted),
            Request(OtherWindowId, OtherWindowExerciseId, "OTHER_WINDOW_DRAFT", RequestStatus.InProgress),
            // Out of scope: belongs to no exercise at all.
            Request(TargetWindowId, null, "ORPHAN_SUBMITTED", RequestStatus.SubmittedUnCommitted),
            Request(TargetWindowId, null, "ORPHAN_DRAFT", RequestStatus.InProgress),
            // Out of scope: already committed.
            Request(TargetWindowId, TargetExerciseId, "ALREADY_COMMITTED", RequestStatus.SubmittedCommitted));

        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task GetRequestsForExercise_returns_only_this_windows_uncommitted_rows_for_this_exercise()
    {
        await SeedAsync();

        var rows = await Repository().GetRequestsForExerciseAsync(
            TargetWindowId, CheckingExerciseType.PupilData, CancellationToken.None);

        Assert.Equal(["IN_SCOPE_SUBMITTED"], rows.Select(r => r.ReferenceNumber).Order());
    }

    [Fact]
    public async Task GetRequestsForExercise_returns_nothing_when_the_window_does_not_run_the_exercise()
    {
        // The other window has no results-enquiry row, so there is no exercise id to match. An
        // empty answer, never an unfiltered one.
        await SeedAsync();

        var rows = await Repository().GetRequestsForExerciseAsync(
            OtherWindowId, CheckingExerciseType.ResultsEnquiry, CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task CountDrafts_counts_only_this_windows_drafts_for_this_exercise()
    {
        await SeedAsync();

        var count = await Repository().CountDraftsForExerciseAsync(
            TargetWindowId, CheckingExerciseType.PupilData, CancellationToken.None);

        // IN_SCOPE_DRAFT + IN_SCOPE_READY. Not the other exercise's, the other window's, or the orphan.
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task MarkDraftsNotSubmitted_moves_only_this_windows_drafts_for_this_exercise()
    {
        await SeedAsync();

        var changed = await Repository().MarkDraftsNotSubmittedForExerciseAsync(
            TargetWindowId, CheckingExerciseType.PupilData, CancellationToken.None);

        Assert.Equal(2, changed);

        await using var ctx = fixture.CreateContext();
        var statuses = await ctx.ChangeRequests
            .AsNoTracking()
            .ToDictionaryAsync(r => r.ReferenceNumber, r => r.Status);

        Assert.Equal(RequestStatus.NotSubmitted, statuses["IN_SCOPE_DRAFT"]);
        Assert.Equal(RequestStatus.NotSubmitted, statuses["IN_SCOPE_READY"]);

        // Everything outside the scope is untouched.
        Assert.Equal(RequestStatus.InProgress, statuses["OTHER_EXERCISE_DRAFT"]);
        Assert.Equal(RequestStatus.InProgress, statuses["OTHER_WINDOW_DRAFT"]);
        Assert.Equal(RequestStatus.InProgress, statuses["ORPHAN_DRAFT"]);
        Assert.Equal(RequestStatus.SubmittedUnCommitted, statuses["ORPHAN_SUBMITTED"]);
        Assert.Equal(RequestStatus.SubmittedUnCommitted, statuses["IN_SCOPE_SUBMITTED"]);
    }

    [Fact]
    public async Task The_count_and_the_update_agree_on_which_drafts_they_mean()
    {
        // The confirmation page must not promise a different number from the one the close cancels.
        await SeedAsync();

        var counted = await Repository().CountDraftsForExerciseAsync(
            TargetWindowId, CheckingExerciseType.PupilData, CancellationToken.None);
        var changed = await Repository().MarkDraftsNotSubmittedForExerciseAsync(
            TargetWindowId, CheckingExerciseType.PupilData, CancellationToken.None);

        Assert.Equal(counted, changed);
    }
}
