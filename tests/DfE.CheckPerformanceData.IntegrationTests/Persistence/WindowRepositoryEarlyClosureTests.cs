using System.Text.Json;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

/// <summary>
/// AB#301022: the write behind an early closure. One guarded statement moves the exercise's end
/// date, the window's own end date is re-derived as the latest exercise end, and one WindowAdmin
/// audit row is written — all or nothing. The guard is a compare-and-set on the end date the admin
/// was shown, so a second close, or a close racing a date edit, writes nothing at all.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class WindowRepositoryEarlyClosureTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Start = new(2026, 7, 19, 0, 0, 0);
    private static readonly DateTime PupilDataEnd = new(2027, 8, 2, 17, 0, 0);
    private static readonly DateTime EnquiryEnd = new(2027, 12, 15, 17, 0, 0);

    // The admin pressed Close at 10:39:00 UK time on 3 Sep 2026 (09:39:00 UTC, BST).
    private static readonly DateTime NewEnd = new(2026, 9, 3, 10, 38, 59);
    private static readonly DateTime ClosedAtUtc = new(2026, 9, 3, 9, 39, 0, DateTimeKind.Utc);

    private readonly Guid _windowId = Guid.NewGuid();
    private readonly Guid _pupilDataId = Guid.NewGuid();
    private readonly Guid _enquiryId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(new CheckingWindow
        {
            Id = _windowId,
            Title = "Key Stage 4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = Start,
            EndDate = EnquiryEnd,
            CheckingExercises =
            [
                new CheckingExercise
                {
                    Id = _pupilDataId,
                    CheckingWindowId = _windowId,
                    ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = Start,
                    EndDate = PupilDataEnd,
                    TabOrder = 0
                },
                new CheckingExercise
                {
                    Id = _enquiryId,
                    CheckingWindowId = _windowId,
                    ExerciseType = CheckingExerciseType.ResultsEnquiry,
                    StartDate = Start,
                    EndDate = EnquiryEnd,
                    TabOrder = 1
                }
            ]
        });
        await ctx.SaveChangesAsync();
    }

    // The window (and, by cascade, its exercises) goes; the audit rows cannot — the table refuses
    // DELETE — so every assertion below is keyed on this test's own window id.
    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.CheckingWindows.Where(w => w.Id == _windowId).ExecuteDeleteAsync();
    }

    private ExerciseEarlyClosure Closure(CheckingExerciseType exercise, DateTime scheduledEnd) => new()
    {
        WindowId = _windowId,
        ExerciseId = exercise == CheckingExerciseType.PupilData ? _pupilDataId : _enquiryId,
        Exercise = exercise,
        ScheduledEnd = scheduledEnd,
        NewEndDate = NewEnd,
        ClosedAtUtc = ClosedAtUtc,
        UserId = "sub-1",
        ClosedByName = "Banks Jamgbadi"
    };

    private async Task<bool> CloseAsync(ExerciseEarlyClosure closure)
    {
        await using var ctx = fixture.CreateContext();
        return await new WindowRepository(ctx).CloseExerciseEarlyAsync(closure, CancellationToken.None);
    }

    private async Task<CheckingWindowDto> ReloadAsync()
    {
        await using var ctx = fixture.CreateContext();
        return (await new WindowRepository(ctx).GetByIdAsync(_windowId, CancellationToken.None))!;
    }

    private async Task<List<DfE.CheckPerformance.Persistence.Entities.AuditEntry>> ClosureAuditRowsAsync()
    {
        await using var ctx = fixture.CreateContext();
        var windowText = _windowId.ToString();
        return await ctx.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == AuditActivities.WindowAdmin && a.EntityId == windowText)
            .ToListAsync();
    }

    [Fact]
    public async Task Closing_the_latest_exercise_moves_its_end_date_and_re_derives_the_windows()
    {
        var closed = await CloseAsync(Closure(CheckingExerciseType.ResultsEnquiry, EnquiryEnd));

        Assert.True(closed);
        var window = await ReloadAsync();
        Assert.Equal(NewEnd, window.FindExercise(CheckingExerciseType.ResultsEnquiry)!.EndDate);
        Assert.Equal(PupilDataEnd, window.FindExercise(CheckingExerciseType.PupilData)!.EndDate);
        // The outer pair is the union of the exercises: pupil data now ends last.
        Assert.Equal(PupilDataEnd, window.EndDate);
        Assert.Equal(Start, window.StartDate);
    }

    [Fact]
    public async Task Closing_an_earlier_exercise_leaves_the_windows_own_end_date_alone()
    {
        var closed = await CloseAsync(Closure(CheckingExerciseType.PupilData, PupilDataEnd));

        Assert.True(closed);
        var window = await ReloadAsync();
        Assert.Equal(NewEnd, window.FindExercise(CheckingExerciseType.PupilData)!.EndDate);
        Assert.Equal(EnquiryEnd, window.EndDate);
    }

    [Fact]
    public async Task Closing_writes_one_window_admin_audit_row_saying_who_which_and_when()
    {
        await CloseAsync(Closure(CheckingExerciseType.ResultsEnquiry, EnquiryEnd));

        var row = Assert.Single(await ClosureAuditRowsAsync());
        Assert.Equal(AuditActivities.ClosedEarlyAction, row.Action);
        Assert.Equal("sub-1", row.UserId);
        Assert.Equal(ClosedAtUtc, row.Timestamp);

        using var payload = JsonDocument.Parse(row.NewValues!);
        var root = payload.RootElement;
        Assert.Equal(_windowId, root.GetProperty("windowId").GetGuid());
        Assert.Equal("Key Stage 4 June", root.GetProperty("windowTitle").GetString());
        Assert.Equal(_enquiryId, root.GetProperty("exerciseId").GetGuid());
        Assert.Equal("ResultsEnquiry", root.GetProperty("exerciseType").GetString());
        Assert.Equal(EnquiryEnd, root.GetProperty("scheduledEnd").GetDateTime());
        Assert.Equal(NewEnd, root.GetProperty("newEndDate").GetDateTime());
        Assert.True(root.GetProperty("closedEarly").GetBoolean());
        Assert.Equal("Banks Jamgbadi", root.GetProperty("closedBy").GetString());
    }

    [Fact]
    public async Task A_stale_scheduled_end_writes_nothing()
    {
        // Someone moved the end date after the confirmation page was rendered.
        var closed = await CloseAsync(Closure(CheckingExerciseType.ResultsEnquiry, EnquiryEnd.AddDays(1)));

        Assert.False(closed);
        var window = await ReloadAsync();
        Assert.Equal(EnquiryEnd, window.FindExercise(CheckingExerciseType.ResultsEnquiry)!.EndDate);
        Assert.Equal(EnquiryEnd, window.EndDate);
        Assert.Empty(await ClosureAuditRowsAsync());
    }

    [Fact]
    public async Task A_second_close_writes_nothing_more()
    {
        var closure = Closure(CheckingExerciseType.ResultsEnquiry, EnquiryEnd);

        Assert.True(await CloseAsync(closure));
        Assert.False(await CloseAsync(closure));

        Assert.Single(await ClosureAuditRowsAsync());
    }

    [Fact]
    public async Task Closing_one_release_leaves_another_of_its_kind_alone()
    {
        // #466: a window may hold two releases of one kind, here with the same end date. The
        // guarded write names the row by id, so it cannot move the other release too.
        var otherId = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
        {
            ctx.CheckingExercises.Add(new CheckingExercise
            {
                Id = otherId,
                CheckingWindowId = _windowId,
                ExerciseType = CheckingExerciseType.ResultsEnquiry,
                StartDate = Start,
                EndDate = EnquiryEnd,
                TabOrder = 2,
                UsesExerciseStorage = true,
                IsEnabled = false
            });
            await ctx.SaveChangesAsync();
        }

        Assert.True(await CloseAsync(Closure(CheckingExerciseType.ResultsEnquiry, EnquiryEnd)));

        var window = await ReloadAsync();
        Assert.Equal(NewEnd, window.Exercises.Single(e => e.Id == _enquiryId).EndDate);
        Assert.Equal(EnquiryEnd, window.Exercises.Single(e => e.Id == otherId).EndDate);
        // The untouched release still ends last, so the window does too.
        Assert.Equal(EnquiryEnd, window.EndDate);
    }

    [Fact]
    public async Task An_exercise_id_from_another_window_writes_nothing()
    {
        var closure = Closure(CheckingExerciseType.PupilData, PupilDataEnd) with { ExerciseId = Guid.NewGuid() };

        Assert.False(await CloseAsync(closure));
        Assert.Empty(await ClosureAuditRowsAsync());
    }

    [Fact]
    public async Task An_unknown_window_writes_nothing()
    {
        var closure = Closure(CheckingExerciseType.PupilData, PupilDataEnd) with { WindowId = Guid.NewGuid() };

        Assert.False(await CloseAsync(closure));
    }

    [Fact]
    public async Task Once_closed_early_the_exercise_is_closed_by_the_one_clock()
    {
        // AC6: the school-facing gates all ask ICheckingExerciseService. At the very instant of
        // the close (one second after the new end date) the closed exercise is shut and has
        // closed, and the other exercise is untouched.
        await CloseAsync(Closure(CheckingExerciseType.ResultsEnquiry, EnquiryEnd));

        var exercises = (await ReloadAsync()).Exercises;
        var clock = new CheckingExerciseService(new FixedTimeProvider(
            new DateTimeOffset(NewEnd.AddSeconds(1), TimeSpan.Zero)));

        Assert.False(clock.IsOpen(exercises, CheckingExerciseType.ResultsEnquiry));
        Assert.True(clock.HasClosed(exercises, CheckingExerciseType.ResultsEnquiry));
        Assert.True(clock.IsOpen(exercises, CheckingExerciseType.PupilData));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
