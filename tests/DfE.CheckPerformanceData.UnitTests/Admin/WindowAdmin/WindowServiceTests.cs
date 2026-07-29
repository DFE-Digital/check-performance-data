using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public class WindowServiceTests
{
    [Fact]
    public async Task CreateAsync_forces_start_to_midnight_and_end_to_five_pm()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.CreateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CheckingWindowDto>());

        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(
            startDate: new DateTime(2027, 1, 1, 9, 30, 15),
            endDate: new DateTime(2027, 2, 1, 9, 30, 15));

        await service.CreateAsync(window, CancellationToken.None);

        await repository.Received(1).CreateAsync(
            Arg.Is<CheckingWindowDto>(w =>
                w.StartDate == new DateTime(2027, 1, 1, 0, 0, 0) &&
                w.EndDate == new DateTime(2027, 2, 1, 17, 0, 0)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_forces_start_to_midnight_and_end_to_five_pm()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();

        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(
            startDate: new DateTime(2027, 1, 1, 23, 59, 59),
            endDate: new DateTime(2027, 2, 1, 3, 15, 0));

        await service.UpdateAsync(window, CancellationToken.None);

        await repository.Received(1).UpdateAsync(
            Arg.Is<CheckingWindowDto>(w =>
                w.StartDate == new DateTime(2027, 1, 1, 0, 0, 0) &&
                w.EndDate == new DateTime(2027, 2, 1, 17, 0, 0)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_brings_a_late_end_time_back_to_five_pm()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.CreateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CheckingWindowDto>());

        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(
            startDate: new DateTime(2027, 1, 1, 0, 0, 0),
            endDate: new DateTime(2027, 2, 1, 22, 0, 0));

        await service.CreateAsync(window, CancellationToken.None);

        await repository.Received(1).CreateAsync(
            Arg.Is<CheckingWindowDto>(w => w.EndDate == new DateTime(2027, 2, 1, 17, 0, 0)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_keeps_dates_unspecified_for_the_timestamp_without_time_zone_column()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        CheckingWindowDto? persisted = null;
        repository.CreateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                persisted = call.Arg<CheckingWindowDto>();
                return persisted;
            });

        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(
            startDate: new DateTime(2027, 1, 1, 9, 30, 15, DateTimeKind.Unspecified),
            endDate: new DateTime(2027, 2, 1, 9, 30, 15, DateTimeKind.Unspecified));

        await service.CreateAsync(window, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(DateTimeKind.Unspecified, persisted!.StartDate.Kind);
        Assert.Equal(DateTimeKind.Unspecified, persisted.EndDate.Kind);
    }

    [Fact]
    public async Task CreateAsync_leaves_already_normalised_dates_unchanged()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.CreateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CheckingWindowDto>());

        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(
            startDate: new DateTime(2027, 1, 1, 0, 0, 0),
            endDate: new DateTime(2027, 2, 1, 17, 0, 0));

        await service.CreateAsync(window, CancellationToken.None);

        await repository.Received(1).CreateAsync(
            Arg.Is<CheckingWindowDto>(w =>
                w.StartDate == new DateTime(2027, 1, 1, 0, 0, 0) &&
                w.EndDate == new DateTime(2027, 2, 1, 17, 0, 0)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllDataAsync_returns_every_window_from_the_repository()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.GetAllWindowsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CheckingWindowDto>
            {
                Window(new DateTime(2020, 1, 1), new DateTime(2020, 2, 1)),
                Window(new DateTime(2027, 1, 1), new DateTime(2027, 2, 1))
            });

        WindowService service = new(repository, new StubTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero)));

        PageResult? result = await service.GetAllDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Windows.Count);
    }

    [Fact]
    public async Task GetAllDataAsync_marks_a_window_open_when_now_is_within_its_dates()
    {
        DateTimeOffset now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.GetAllWindowsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CheckingWindowDto>
            {
                Window(now.DateTime.AddDays(-1), now.DateTime.AddDays(1))
            });

        WindowService service = new(repository, new StubTimeProvider(now));

        PageResult? result = await service.GetAllDataAsync(CancellationToken.None);

        Assert.True(result!.Windows.Single().IsOpen);
    }

    [Theory]
    [InlineData(-10, -5)] // already ended
    [InlineData(5, 10)]   // not yet started
    public async Task GetAllDataAsync_marks_a_window_closed_when_now_is_outside_its_dates(int startOffsetDays, int endOffsetDays)
    {
        DateTimeOffset now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.GetAllWindowsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CheckingWindowDto>
            {
                Window(now.DateTime.AddDays(startOffsetDays), now.DateTime.AddDays(endOffsetDays))
            });

        WindowService service = new(repository, new StubTimeProvider(now));

        PageResult? result = await service.GetAllDataAsync(CancellationToken.None);

        Assert.False(result!.Windows.Single().IsOpen);
    }

    private static CheckingWindowDto Window(DateTime startDate, DateTime endDate) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = "Existing window",
            StartDate = startDate,
            EndDate = endDate,
            KeyStage = KeyStages.KS2,
            CheckingWindowType = CheckingWindowType.KS2
        };

    private sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
