using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

/// <summary>
/// AB#298317: pins the <c>NextOpportunity</c> column mapping through the window repository. The
/// date must round-trip through GetByIdAsync/UpdateAsync (including back to null, because an
/// admin can clear it) and survive CreateAsync, so the admin edit page can set it and the landing
/// page and Check your pupil data can read it back.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class WindowRepositoryNextOpportunityTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime October2027 = new(2027, 10, 1);

    private readonly Guid _windowId = Guid.NewGuid();
    private readonly List<Guid> _createdWindowIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        var ids = _createdWindowIds.Concat([_windowId]).ToList();
        await ctx.CheckingWindows.Where(w => ids.Contains(w.Id)).ExecuteDeleteAsync();
    }

    private async Task SeedAsync(DateTime? nextOpportunity)
    {
        await using var seedCtx = fixture.CreateContext();
        seedCtx.CheckingWindows.Add(new()
        {
            Id = _windowId,
            Title = "16 to 19",
            KeyStage = Domain.Enums.KeyStages.Post16,
            CheckingWindowType = Domain.Enums.CheckingWindowType.Post16,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(14),
            NextOpportunity = nextOpportunity
        });
        await seedCtx.SaveChangesAsync();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsSeededNextOpportunity()
    {
        await SeedAsync(October2027);

        await using var ctx = fixture.CreateContext();
        var window = await new WindowRepository(ctx).GetByIdAsync(_windowId, CancellationToken.None);

        Assert.NotNull(window);
        Assert.Equal(October2027, window!.NextOpportunity);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullWhenNotSet()
    {
        await SeedAsync(null);

        await using var ctx = fixture.CreateContext();
        var window = await new WindowRepository(ctx).GetByIdAsync(_windowId, CancellationToken.None);

        Assert.Null(window!.NextOpportunity);
    }

    [Fact]
    public async Task UpdateAsync_PersistsNextOpportunity_AndCanClearItAgain()
    {
        await SeedAsync(null);

        await using (var ctx = fixture.CreateContext())
        {
            var sut = new WindowRepository(ctx);
            var window = (await sut.GetByIdAsync(_windowId, CancellationToken.None))!;
            window.NextOpportunity = October2027;
            await sut.UpdateAsync(window, CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var reloaded = await new WindowRepository(ctx).GetByIdAsync(_windowId, CancellationToken.None);
            Assert.Equal(October2027, reloaded!.NextOpportunity);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var sut = new WindowRepository(ctx);
            var window = (await sut.GetByIdAsync(_windowId, CancellationToken.None))!;
            window.NextOpportunity = null;
            await sut.UpdateAsync(window, CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var reloaded = await new WindowRepository(ctx).GetByIdAsync(_windowId, CancellationToken.None);
            Assert.Null(reloaded!.NextOpportunity);
        }
    }

    [Fact]
    public async Task CreateAsync_PersistsNextOpportunity()
    {
        var created = await new WindowRepository(fixture.CreateContext())
            .CreateAsync(new CheckingWindowDto
            {
                Id = _windowId,
                Title = "16 to 19",
                KeyStage = Domain.Enums.KeyStages.Post16,
                CheckingWindowType = Domain.Enums.CheckingWindowType.Post16,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(14),
                NextOpportunity = October2027
            }, CancellationToken.None);

        Assert.Equal(October2027, created.NextOpportunity);
        _createdWindowIds.Add(created.Id);

        await using var ctx = fixture.CreateContext();
        var reloaded = await new WindowRepository(ctx).GetByIdAsync(created.Id, CancellationToken.None);
        Assert.Equal(October2027, reloaded!.NextOpportunity);
    }
}
