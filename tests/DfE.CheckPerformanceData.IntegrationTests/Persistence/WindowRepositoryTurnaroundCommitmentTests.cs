using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

/// <summary>
/// Pins the <c>TurnaroundCommitment</c> column mapping through the window repository: the value
/// must round-trip through <see cref="WindowRepository.GetByIdAsync"/>/<see cref="WindowRepository.UpdateAsync"/>
/// and survive <see cref="WindowRepository.CreateAsync"/> so the admin edit page can set it and the
/// email substitutions can read it back.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class WindowRepositoryTurnaroundCommitmentTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly Guid _windowId = Guid.NewGuid();
    private readonly List<Guid> _createdWindowIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        var ids = _createdWindowIds.Concat([_windowId]).ToList();
        await ctx.CheckingWindows.Where(w => ids.Contains(w.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsSeededTurnaroundCommitment()
    {
        await using var seedCtx = fixture.CreateContext();
        seedCtx.CheckingWindows.Add(new()
        {
            Id = _windowId,
            Title = "KS4 June",
            KeyStage = Domain.Enums.KeyStages.KS4,
            CheckingWindowType = Domain.Enums.CheckingWindowType.KS4June,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(14),
            TurnaroundCommitment = "updated in the Autumn"
        });
        await seedCtx.SaveChangesAsync();

        await using var ctx = fixture.CreateContext();
        var sut = new WindowRepository(ctx);

        var window = await sut.GetByIdAsync(_windowId, CancellationToken.None);

        Assert.NotNull(window);
        Assert.Equal("updated in the Autumn", window!.TurnaroundCommitment);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChangedTurnaroundCommitment()
    {
        await using var seedCtx = fixture.CreateContext();
        seedCtx.CheckingWindows.Add(new()
        {
            Id = _windowId,
            Title = "KS4 June",
            KeyStage = Domain.Enums.KeyStages.KS4,
            CheckingWindowType = Domain.Enums.CheckingWindowType.KS4June,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(14),
            TurnaroundCommitment = string.Empty
        });
        await seedCtx.SaveChangesAsync();

        await using var ctx = fixture.CreateContext();
        var sut = new WindowRepository(ctx);
        var window = await sut.GetByIdAsync(_windowId, CancellationToken.None);
        Assert.NotNull(window);
        window!.TurnaroundCommitment = "updated in the Spring";

        await sut.UpdateAsync(window, CancellationToken.None);

        await using var readCtx = fixture.CreateContext();
        var reloaded = await new WindowRepository(readCtx)
            .GetByIdAsync(_windowId, CancellationToken.None);
        Assert.Equal("updated in the Spring", reloaded!.TurnaroundCommitment);
    }

    [Fact]
    public async Task CreateAsync_PersistsTurnaroundCommitment()
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
                TurnaroundCommitment = "updated in the Spring",
                Datasets = []
            }, CancellationToken.None);

        Assert.Equal("updated in the Spring", created.TurnaroundCommitment);
        _createdWindowIds.Add(created.Id);

        await using var ctx = fixture.CreateContext();
        var reloaded = await new WindowRepository(ctx)
            .GetByIdAsync(created.Id, CancellationToken.None);
        Assert.Equal("updated in the Spring", reloaded!.TurnaroundCommitment);
    }
}
