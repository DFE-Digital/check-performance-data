using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// AB#307773: the extension tables, the guarded writes behind them and the window end date they move.
[Collection(nameof(PostgresCollection))]
public sealed class ExerciseExtensionRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0);
    private static readonly DateTime ExerciseEnd = new(2026, 10, 1, 17, 0, 0);
    private static readonly DateTime ExtensionEnd = new(2026, 10, 8, 17, 0, 0);

    private readonly Guid _windowId = Guid.NewGuid();
    private readonly Guid _exerciseId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        ctx.CheckingWindows.Add(new CheckingWindow
        {
            Id = _windowId, Title = "KS4 June", KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June, StartDate = Start, EndDate = ExerciseEnd,
            CheckingExercises =
            [
                new CheckingExercise
                {
                    Id = _exerciseId, CheckingWindowId = _windowId, ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = Start, EndDate = ExerciseEnd, TabOrder = 0, TabName = "Pupils", IsEnabled = true
                }
            ]
        });
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.CheckingWindows.Where(w => w.Id == _windowId).ExecuteDeleteAsync();
    }

    private static CheckingExerciseExtension Extension(Guid exerciseId, DateTime end, params string[] laestabs) => new()
    {
        CheckingExerciseId = exerciseId, EndDate = end, CreatedAt = new DateTime(2026, 9, 20, 9, 0, 0), CreatedBy = "Ada Admin",
        Schools = laestabs.Select(l => new CheckingExerciseExtensionSchool { CheckingExerciseId = exerciseId, Laestab = l }).ToList()
    };

    [Fact]
    public async Task An_extension_and_its_schools_round_trip_onto_the_window_dto()
    {
        await using (var ctx = fixture.CreateContext())
        {
            ctx.CheckingExerciseExtensions.Add(Extension(_exerciseId, ExtensionEnd, "9334290", "8604070"));
            await ctx.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext();
        var window = await new WindowRepository(read).GetByIdAsync(_windowId, CancellationToken.None);

        var extension = Assert.Single(window!.Exercises.Single().Extensions);
        Assert.Equal(ExtensionEnd, extension.EndDate);
        Assert.Equal(["8604070", "9334290"], extension.Laestabs);
        Assert.Equal("Ada Admin", extension.CreatedBy);
        Assert.Null(extension.RequestsSentAt);
    }

    [Fact]
    public async Task A_school_cannot_be_in_two_extensions_of_one_exercise()
    {
        await using var ctx = fixture.CreateContext();
        ctx.CheckingExerciseExtensions.Add(Extension(_exerciseId, ExtensionEnd, "9334290"));
        ctx.CheckingExerciseExtensions.Add(Extension(_exerciseId, ExtensionEnd.AddDays(1), "9334290"));

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task An_add_naming_a_school_already_in_another_extension_is_refused_by_the_index_and_writes_nothing()
    {
        await using (var first = fixture.CreateContext())
            await Repository(first).AddAsync(NewExt("9334290"), CancellationToken.None);

        await using var ctx = fixture.CreateContext();
        await Assert.ThrowsAsync<ExtensionSchoolTakenException>(
            () => Repository(ctx).AddAsync(NewExt("8604070", "9334290"), CancellationToken.None));

        await using var check = fixture.CreateContext();
        Assert.Equal(1, await check.CheckingExerciseExtensions.CountAsync(x => x.CheckingExerciseId == _exerciseId));
        Assert.Equal(0, await check.CheckingExerciseExtensionSchools.CountAsync(s => s.CheckingExerciseId == _exerciseId && s.Laestab == "8604070"));
        Assert.Equal(1, await check.AuditEntries.CountAsync(a => a.EntityId == _windowId.ToString() && a.Action == AuditActivities.ExtensionAddedAction));
    }

    [Fact]
    public async Task An_edit_taking_a_school_from_another_extension_is_refused_by_the_index_and_rolls_back_the_version()
    {
        Guid b;
        await using (var ctx = fixture.CreateContext())
        {
            await Repository(ctx).AddAsync(NewExt("9334290"), CancellationToken.None);
            b = await Repository(ctx).AddAsync(NewExt("8604070"), CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            await Assert.ThrowsAsync<ExtensionSchoolTakenException>(
                () => Repository(ctx).UpdateAsync(Edit(b, 0, ExtensionEnd.AddDays(1), "8604070", "9334290"), CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        var row = await check.CheckingExerciseExtensions.SingleAsync(x => x.Id == b);
        Assert.Equal(0, row.Version);
        Assert.Equal(ExtensionEnd, row.EndDate);
        Assert.Equal(["8604070"], await check.CheckingExerciseExtensionSchools.Where(s => s.CheckingExerciseExtensionId == b).Select(s => s.Laestab).ToListAsync());
        Assert.Equal(0, await check.AuditEntries.CountAsync(x => x.EntityId == _windowId.ToString() && x.Action == AuditActivities.ExtensionEditedAction));
    }

    [Fact]
    public async Task Deleting_the_exercise_deletes_its_extensions_and_schools()
    {
        await using (var ctx = fixture.CreateContext())
        {
            ctx.CheckingExerciseExtensions.Add(Extension(_exerciseId, ExtensionEnd, "9334290"));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateContext())
            await new ExerciseDeletionRepository(ctx).DeleteAsync(_windowId, _exerciseId, CancellationToken.None);

        await using var check = fixture.CreateContext();
        Assert.Equal(0, await check.CheckingExerciseExtensions.CountAsync(x => x.CheckingExerciseId == _exerciseId));
        Assert.Equal(0, await check.CheckingExerciseExtensionSchools.CountAsync(x => x.CheckingExerciseId == _exerciseId));
    }

    private ExerciseExtensionRepository Repository(PortalDbContext ctx) => new(ctx);
    private static readonly ExtensionActor Ada = new("user-1", "Ada Admin");

    private NewExtension NewExt(params string[] laestabs) => new()
    {
        WindowId = _windowId, ExerciseId = _exerciseId, Exercise = CheckingExerciseType.PupilData,
        EndDate = ExtensionEnd, Laestabs = laestabs, Actor = Ada,
        CreatedAtUtc = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc)
    };

    private ExtensionEdit Edit(Guid id, int version, DateTime newEnd, params string[] laestabs) => new()
    {
        WindowId = _windowId, ExerciseId = _exerciseId, Exercise = CheckingExerciseType.PupilData, ExtensionId = id,
        ExpectedVersion = version, PreviousEndDate = ExtensionEnd, NewEndDate = newEnd, Laestabs = laestabs, Actor = Ada,
        EditedAtUtc = new DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc)
    };

    private ExtensionEarlyClosure Closure(Guid id, DateTime scheduledEnd) => new()
    {
        WindowId = _windowId, ExerciseId = _exerciseId, Exercise = CheckingExerciseType.PupilData, ExtensionId = id,
        ScheduledEnd = scheduledEnd, NewEndDate = scheduledEnd.AddDays(-1), Actor = Ada, ClosedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task Add_writes_the_rows_and_the_audit_row_and_leaves_the_window_end()
    {
        Guid id;
        await using (var ctx = fixture.CreateContext())
            id = await Repository(ctx).AddAsync(new NewExtension
            {
                WindowId = _windowId, ExerciseId = _exerciseId, Exercise = CheckingExerciseType.PupilData,
                EndDate = ExtensionEnd, Laestabs = ["9334290", "8604070"], Actor = Ada,
                CreatedAtUtc = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc)
            }, CancellationToken.None);

        await using var check = fixture.CreateContext();
        var extension = await check.CheckingExerciseExtensions.Include(x => x.Schools).SingleAsync(x => x.Id == id);
        Assert.Equal(2, extension.Schools.Count);
        Assert.All(extension.Schools, s => Assert.Equal(_exerciseId, s.CheckingExerciseId));
        Assert.Equal(ExerciseEnd, (await check.CheckingWindows.SingleAsync(w => w.Id == _windowId)).EndDate);
        var audit = await check.AuditEntries.SingleAsync(a => a.EntityType == "WindowAdmin" && a.EntityId == _windowId.ToString() && a.Action == "ExtensionAdded");
        Assert.Equal("user-1", audit.UserId);
        Assert.Contains("\"extensionId\":\"" + id, audit.NewValues);
        Assert.Contains("\"schoolCount\":2", audit.NewValues);
        Assert.Contains("\"actorName\":\"Ada Admin\"", audit.NewValues);
    }

    [Fact]
    public async Task Add_writes_only_the_window_admin_row_and_no_generic_audit_rows()
    {
        // AB#307773 review I4: the hand-written WindowAdmin row is the record. Generic capture would
        // add one row for the extension and one per school, saying nothing that row does not.
        Guid id;
        await using (var ctx = fixture.CreateContext())
            id = await Repository(ctx).AddAsync(NewExt("9334290", "8604070"), CancellationToken.None);

        await using var check = fixture.CreateContext();
        var schoolIds = await check.CheckingExerciseExtensionSchools
            .Where(s => s.CheckingExerciseExtensionId == id).Select(s => s.Id.ToString()).ToListAsync();
        Assert.Equal(2, schoolIds.Count);
        var entityIds = schoolIds.Append(id.ToString()).ToList();

        Assert.Equal(1, await check.AuditEntries.CountAsync(a => a.EntityType == "WindowAdmin" && a.EntityId == _windowId.ToString()));
        Assert.Equal(0, await check.AuditEntries.CountAsync(a => entityIds.Contains(a.EntityId)));
    }

    [Fact]
    public async Task Update_is_a_compare_and_set_on_the_version_and_records_the_school_diff()
    {
        Guid id;
        await using (var ctx = fixture.CreateContext())
            id = await Repository(ctx).AddAsync(NewExt("9334290", "8604070"), CancellationToken.None);

        await using (var ctx = fixture.CreateContext())
        {
            var stale = await Repository(ctx).UpdateAsync(Edit(id, version: 7, newEnd: ExtensionEnd.AddDays(9), "9334290"), CancellationToken.None);
            Assert.False(stale);
            var ok = await Repository(ctx).UpdateAsync(Edit(id, version: 0, newEnd: ExtensionEnd.AddDays(9), "9334290", "1234567"), CancellationToken.None);
            Assert.True(ok);
        }

        await using var check = fixture.CreateContext();
        var extension = await check.CheckingExerciseExtensions.Include(x => x.Schools).SingleAsync(x => x.Id == id);
        Assert.Equal(1, extension.Version);
        Assert.Equal(ExtensionEnd.AddDays(9), extension.EndDate);
        Assert.Equal(["1234567", "9334290"], extension.Schools.Select(s => s.Laestab).OrderBy(l => l));
        Assert.Equal(ExerciseEnd, (await check.CheckingWindows.SingleAsync(w => w.Id == _windowId)).EndDate);
        var audit = await check.AuditEntries.SingleAsync(a => a.Action == "ExtensionEdited" && a.EntityId == _windowId.ToString());
        Assert.Contains("\"schoolsAdded\":[\"1234567\"]", audit.NewValues);
        Assert.Contains("\"schoolsRemoved\":[\"8604070\"]", audit.NewValues);
    }

    [Fact]
    public async Task Two_edits_that_change_only_the_schools_cannot_both_pass()
    {
        // Review finding 3 (15 Oct): both admins read version 0 and change only the list. The
        // second save must be refused, or it silently undoes the first.
        Guid id;
        await using (var ctx = fixture.CreateContext())
            id = await Repository(ctx).AddAsync(NewExt("9334290"), CancellationToken.None);

        bool first, second;
        await using (var ctx = fixture.CreateContext())
            first = await Repository(ctx).UpdateAsync(Edit(id, version: 0, newEnd: ExtensionEnd, "9334290", "8604070"), CancellationToken.None);
        await using (var ctx = fixture.CreateContext())
            second = await Repository(ctx).UpdateAsync(Edit(id, version: 0, newEnd: ExtensionEnd, "9334290", "1234567"), CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
        await using var check = fixture.CreateContext();
        var schools = await check.CheckingExerciseExtensionSchools.Where(s => s.CheckingExerciseExtensionId == id).Select(s => s.Laestab).ToListAsync();
        Assert.Equal(["8604070", "9334290"], schools.OrderBy(l => l));
        Assert.Equal(1, (await check.CheckingExerciseExtensions.SingleAsync(x => x.Id == id)).Version);
    }

    [Fact]
    public async Task Close_early_bumps_the_version_so_a_stale_edit_is_refused()
    {
        Guid id;
        await using (var ctx = fixture.CreateContext())
            id = await Repository(ctx).AddAsync(NewExt("9334290"), CancellationToken.None);

        await using (var ctx = fixture.CreateContext())
            Assert.True(await Repository(ctx).CloseEarlyAsync(Closure(id, scheduledEnd: ExtensionEnd), CancellationToken.None));
        await using (var ctx = fixture.CreateContext())
            Assert.False(await Repository(ctx).UpdateAsync(Edit(id, version: 0, newEnd: ExtensionEnd.AddDays(9), "9334290"), CancellationToken.None));

        await using var check = fixture.CreateContext();
        Assert.Equal(1, (await check.CheckingExerciseExtensions.SingleAsync(x => x.Id == id)).Version);
    }

    [Fact]
    public async Task Update_keeps_the_ids_of_the_schools_it_does_not_remove()
    {
        Guid id;
        await using (var ctx = fixture.CreateContext())
            id = await Repository(ctx).AddAsync(NewExt("9334290", "8604070"), CancellationToken.None);
        Guid kept;
        await using (var ctx = fixture.CreateContext())
            kept = (await ctx.CheckingExerciseExtensionSchools.SingleAsync(s => s.CheckingExerciseExtensionId == id && s.Laestab == "9334290")).Id;

        // The end date is unchanged; the version guard applies (version 0 matches the stored one).
        await using (var ctx = fixture.CreateContext())
            Assert.True(await Repository(ctx).UpdateAsync(Edit(id, 0, ExtensionEnd, "9334290", "1234567"), CancellationToken.None));

        await using var check = fixture.CreateContext();
        Assert.Equal(kept, (await check.CheckingExerciseExtensionSchools.SingleAsync(s => s.CheckingExerciseExtensionId == id && s.Laestab == "9334290")).Id);
        Assert.Equal(_exerciseId, (await check.CheckingExerciseExtensionSchools.SingleAsync(s => s.CheckingExerciseExtensionId == id && s.Laestab == "1234567")).CheckingExerciseId);
        Assert.False(await check.CheckingExerciseExtensionSchools.AnyAsync(s => s.CheckingExerciseExtensionId == id && s.Laestab == "8604070"));
    }

    [Fact]
    public async Task Close_early_moves_the_end_once_and_writes_one_row()
    {
        Guid id;
        await using (var ctx = fixture.CreateContext())
            id = await Repository(ctx).AddAsync(NewExt("9334290"), CancellationToken.None);
        // Closed before the exercise's own end (decision 2), so the window falls back to that end.
        var closure = new ExtensionEarlyClosure
        {
            WindowId = _windowId, ExerciseId = _exerciseId, Exercise = CheckingExerciseType.PupilData, ExtensionId = id,
            ScheduledEnd = ExtensionEnd, NewEndDate = new DateTime(2026, 9, 29, 10, 38, 59), Actor = Ada,
            ClosedAtUtc = new DateTime(2026, 9, 29, 9, 39, 0, DateTimeKind.Utc)
        };

        await using (var ctx = fixture.CreateContext())
        {
            Assert.True(await Repository(ctx).CloseEarlyAsync(closure, CancellationToken.None));
            Assert.False(await Repository(ctx).CloseEarlyAsync(closure, CancellationToken.None));
        }

        await using var check = fixture.CreateContext();
        Assert.Equal(closure.NewEndDate, (await check.CheckingExerciseExtensions.SingleAsync(x => x.Id == id)).EndDate);
        // The window's end falls back to the exercise's own end, which is later than the close.
        Assert.Equal(ExerciseEnd, (await check.CheckingWindows.SingleAsync(w => w.Id == _windowId)).EndDate);
        Assert.Equal(1, await check.AuditEntries.CountAsync(a => a.Action == "ExtensionClosedEarly" && a.EntityId == _windowId.ToString()));
    }

    [Fact]
    public async Task Mark_requests_sent_sets_the_stamp_once()
    {
        Guid id;
        await using (var ctx = fixture.CreateContext())
            id = await Repository(ctx).AddAsync(NewExt("9334290"), CancellationToken.None);

        await using var ctx2 = fixture.CreateContext();
        var at = new DateTime(2026, 10, 8, 19, 0, 0, DateTimeKind.Utc);
        Assert.True(await Repository(ctx2).MarkRequestsSentAsync(id, at, CancellationToken.None));
        Assert.False(await Repository(ctx2).MarkRequestsSentAsync(id, at.AddHours(1), CancellationToken.None));
        Assert.Equal(DateTime.SpecifyKind(at, DateTimeKind.Unspecified), (await ctx2.CheckingExerciseExtensions.AsNoTracking().SingleAsync(x => x.Id == id)).RequestsSentAt);
    }

    // The audit log reads these rows back through WindowAdminAuditPayload, so the payload names
    // have to match what it parses: a misspelt one would list the row with its detail missing.
    [Fact]
    public async Task The_audit_log_lists_the_four_extension_rows_with_their_fields()
    {
        var ct = CancellationToken.None;
        var closedEnd = new DateTime(2026, 10, 5, 10, 38, 59);
        Guid id;
        await using (var ctx = fixture.CreateContext())
            id = await Repository(ctx).AddAsync(NewExt("9334290", "8604070"), ct);
        await using (var ctx = fixture.CreateContext())
            Assert.True(await Repository(ctx).UpdateAsync(Edit(id, 0, ExtensionEnd.AddDays(9), "9334290", "1234567"), ct));
        await using (var ctx = fixture.CreateContext())
            Assert.True(await Repository(ctx).CloseEarlyAsync(new ExtensionEarlyClosure
            {
                WindowId = _windowId, ExerciseId = _exerciseId, Exercise = CheckingExerciseType.PupilData, ExtensionId = id,
                ScheduledEnd = ExtensionEnd.AddDays(9), NewEndDate = closedEnd, Actor = Ada,
                ClosedAtUtc = new DateTime(2026, 10, 5, 9, 39, 0, DateTimeKind.Utc)
            }, ct));
        await using (var ctx = fixture.CreateContext())
            await Repository(ctx).RecordRequestsSentAsync(new ExtensionRequestsSent
            {
                WindowId = _windowId, ExerciseId = _exerciseId, Exercise = CheckingExerciseType.PupilData, ExtensionId = id,
                ExtensionEnd = closedEnd, RequestsSent = 3, DraftsCancelled = 1, Actor = Ada,
                SentAtUtc = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc)
            }, ct);

        await using var read = fixture.CreateContext();
        var page = await new AuditLogRepository(read).ListAsync(new AuditLogFilter(null, _windowId, AuditOutcome.Success), 1, 50, ct);

        Assert.Equal(
            [AuditActivities.ExtensionRequestsSentAction, AuditActivities.ExtensionClosedEarlyAction,
             AuditActivities.ExtensionEditedAction, AuditActivities.ExtensionAddedAction],
            page.Rows.Select(r => r.Action));
        Assert.All(page.Rows, r =>
        {
            Assert.Equal(id, r.ExtensionId);
            Assert.Equal("user-1", r.UserId);
            Assert.Equal("Ada Admin", r.UserName);
            Assert.Equal("PupilData", r.ExerciseType);
            Assert.Equal("KS4 June", r.WindowTitle);
        });

        var added = page.Rows.Single(r => r.Action == AuditActivities.ExtensionAddedAction);
        Assert.Equal(ExtensionEnd, added.EndDate);
        Assert.Equal(2, added.SchoolCount);

        var edited = page.Rows.Single(r => r.Action == AuditActivities.ExtensionEditedAction);
        Assert.Equal(ExtensionEnd.AddDays(9), edited.EndDate);
        Assert.Equal(2, edited.SchoolCount);
        Assert.Equal(["1234567"], edited.SchoolsAdded!);
        Assert.Equal(["8604070"], edited.SchoolsRemoved!);

        var closed = page.Rows.Single(r => r.Action == AuditActivities.ExtensionClosedEarlyAction);
        Assert.Equal(closedEnd, closed.EndDate);

        var sent = page.Rows.Single(r => r.Action == AuditActivities.ExtensionRequestsSentAction);
        Assert.Equal(closedEnd, sent.EndDate);
        Assert.Equal(3, sent.RequestsSent);
        Assert.Equal(1, sent.DraftsCancelled);
    }

    // Review finding 1 (15 Oct): the window's stored end is the exercises' own latest end, whatever
    // extensions exist. The school's end is computed from the extensions on every read.
    [Fact]
    public async Task No_extension_write_moves_the_window_end()
    {
        Guid id;
        await using (var ctx = fixture.CreateContext())
            id = await Repository(ctx).AddAsync(NewExt("9334290"), CancellationToken.None);
        await using (var ctx = fixture.CreateContext())
            await Repository(ctx).UpdateAsync(Edit(id, version: 0, newEnd: ExtensionEnd.AddDays(9), "9334290"), CancellationToken.None);

        await using var check = fixture.CreateContext();
        Assert.Equal(ExerciseEnd, (await check.CheckingWindows.SingleAsync(w => w.Id == _windowId)).EndDate);
        Assert.Equal(ExtensionEnd.AddDays(9), (await check.CheckingExerciseExtensions.SingleAsync(x => x.Id == id)).EndDate);
    }

    [Fact]
    public async Task The_admin_window_list_carries_the_extension_and_its_schools()
    {
        await using (var ctx = fixture.CreateContext())
            await Repository(ctx).AddAsync(NewExt("9334290", "8604070"), CancellationToken.None);

        await using var read = fixture.CreateContext();
        var windows = await new WindowRepository(read).GetAllWindowsAsync(CancellationToken.None);

        var extension = Assert.Single(windows.Single(w => w.Id == _windowId).Exercises.Single().Extensions);
        Assert.Equal(ExtensionEnd, extension.EndDate);
        Assert.Equal(["8604070", "9334290"], extension.Laestabs);
    }

    [Fact]
    public async Task The_landing_page_read_carries_the_extension_and_its_schools()
    {
        await using (var ctx = fixture.CreateContext())
            await Repository(ctx).AddAsync(NewExt("9334290", "8604070"), CancellationToken.None);

        await using var read = fixture.CreateContext();
        var windows = await new LandingPageRepository(read, NullLogger<LandingPageRepository>.Instance)
            .GetStartedWindowsAsync(Start.AddDays(1), "933/4290", CancellationToken.None);

        var extension = Assert.Single(windows.Single(w => w.Id == _windowId).Exercises.Single().Extensions);
        Assert.Equal(ExtensionEnd, extension.EndDate);
        Assert.Equal(["8604070", "9334290"], extension.Laestabs);
    }

    [Fact]
    public async Task The_check_your_pupil_data_window_read_carries_the_extension_and_its_schools()
    {
        await using (var ctx = fixture.CreateContext())
            await Repository(ctx).AddAsync(NewExt("9334290", "8604070"), CancellationToken.None);

        await using var read = fixture.CreateContext();
        // The window read touches neither the blob client nor the cache.
        var window = await new CheckYourPupilDataRepository(read, Substitute.For<IPupilDataBlobClient>(),
            new MemoryCache(new MemoryCacheOptions())).GetCheckingWindowAsync(_windowId);

        var extension = Assert.Single(window.Exercises.Single().Extensions);
        Assert.Equal(ExtensionEnd, extension.EndDate);
        Assert.Equal(["8604070", "9334290"], extension.Laestabs);
    }
}
