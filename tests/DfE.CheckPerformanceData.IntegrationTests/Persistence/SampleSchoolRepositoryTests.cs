using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// #937: the sample school list of a window, against the real unique index and foreign key.
[Collection(nameof(PostgresCollection))]
public sealed class SampleSchoolRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    // A Utc kind, as the service gives from TimeProvider.GetUtcNow().UtcDateTime.
    private static readonly DateTime AddedAt = new(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

    private readonly Guid _windowId = Guid.NewGuid();
    private readonly Guid _otherWindowId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        foreach (var id in new[] { _windowId, _otherWindowId })
        {
            ctx.CheckingWindows.Add(new CheckingWindow
            {
                Id = id, Title = "KS2", KeyStage = KeyStages.KS2, CheckingWindowType = CheckingWindowType.KS2,
                StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 10, 1)
            });
        }
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.CheckingWindows.Where(w => w.Id == _windowId || w.Id == _otherWindowId).ExecuteDeleteAsync();
    }

    private NewSampleSchool School(Guid windowId, string laestab) => new(windowId, laestab, "123456", AddedAt, "Ada Admin");

    [Fact]
    public async Task A_window_lists_a_school_once_and_another_window_can_list_it_too()
    {
        await using var ctx = fixture.CreateContext();
        var repository = new SampleSchoolRepository(ctx);

        Assert.True(await repository.AddAsync(School(_windowId, "9334290"), CancellationToken.None));
        Assert.False(await repository.AddAsync(School(_windowId, "9334290"), CancellationToken.None));
        Assert.True(await repository.AddAsync(School(_otherWindowId, "9334290"), CancellationToken.None));

        var listed = Assert.Single(await repository.ListAsync(_windowId, CancellationToken.None));
        Assert.Equal(("9334290", "123456", "Ada Admin", AddedAt), (listed.Laestab, listed.Urn, listed.AddedBy, listed.AddedAt));
    }

    [Fact]
    public async Task Each_add_and_remove_is_in_the_audit_log()
    {
        await using var ctx = fixture.CreateContext();
        var repository = new SampleSchoolRepository(ctx);

        await repository.AddAsync(School(_windowId, "9334290"), CancellationToken.None);
        var id = (await repository.ListAsync(_windowId, CancellationToken.None)).Single().Id;
        await repository.RemoveAsync(_windowId, id, CancellationToken.None);

        var actions = await ctx.AuditEntries
            .Where(a => a.EntityType == nameof(CheckingWindowSampleSchool) && a.EntityId == id.ToString())
            .Select(a => a.Action)
            .ToListAsync();
        Assert.Equal(["Delete", "Insert"], actions.Order());
    }

    [Fact]
    public async Task Remove_does_not_touch_a_school_of_another_window()
    {
        await using var ctx = fixture.CreateContext();
        var repository = new SampleSchoolRepository(ctx);
        await repository.AddAsync(School(_otherWindowId, "9334290"), CancellationToken.None);
        var id = (await repository.ListAsync(_otherWindowId, CancellationToken.None)).Single().Id;

        Assert.False(await repository.RemoveAsync(_windowId, id, CancellationToken.None));
        Assert.Single(await repository.ListAsync(_otherWindowId, CancellationToken.None));
    }

    [Fact]
    public async Task The_admin_window_load_carries_the_list_in_laestab_order()
    {
        await using var ctx = fixture.CreateContext();
        var repository = new SampleSchoolRepository(ctx);
        await repository.AddAsync(School(_windowId, "9334291"), CancellationToken.None);
        await repository.AddAsync(School(_windowId, "9334290"), CancellationToken.None);
        await repository.AddAsync(School(_otherWindowId, "9334292"), CancellationToken.None);

        var window = await new WindowRepository(ctx).GetByIdAsync(_windowId, CancellationToken.None);

        Assert.Equal(["9334290", "9334291"], window!.SampleLaestabs);
    }

    [Fact]
    public async Task The_list_goes_with_its_window()
    {
        await using (var ctx = fixture.CreateContext())
            await new SampleSchoolRepository(ctx).AddAsync(School(_windowId, "9334290"), CancellationToken.None);

        await using (var ctx = fixture.CreateContext())
        {
            await ctx.CheckingWindows.Where(w => w.Id == _windowId).ExecuteDeleteAsync();
            Assert.False(await ctx.CheckingWindowSampleSchools.AnyAsync(s => s.CheckingWindowId == _windowId));
        }
    }
}
