using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

public sealed class WindowStatusServiceTests
{
    private static readonly TimeZoneInfo LondonTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    [Fact]
    public void IsOpen_is_true_at_the_window_start()
    {
        DateTimeOffset now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
        WindowStatusService service = new(new FixedTimeProvider(now));
        CheckingWindowDto window = Window(
            new DateTime(2026, 1, 15, 9, 0, 0),
            new DateTime(2026, 1, 15, 17, 0, 0));

        Assert.True(service.IsOpen(window));
    }

    [Fact]
    public void IsOpen_is_true_at_the_window_end()
    {
        DateTimeOffset now = new(2026, 1, 15, 17, 0, 0, TimeSpan.Zero);
        WindowStatusService service = new(new FixedTimeProvider(now));
        CheckingWindowDto window = Window(
            new DateTime(2026, 1, 15, 9, 0, 0),
            new DateTime(2026, 1, 15, 17, 0, 0));

        Assert.True(service.IsOpen(window));
    }

    [Fact]
    public void IsOpen_uses_UK_wall_clock_time_during_BST()
    {
        // 11:30 UTC is 12:30 in London. A window ending at 12:00 UK wall-clock time is closed.
        DateTimeOffset now = new(2026, 7, 15, 11, 30, 0, TimeSpan.Zero);
        WindowStatusService service = new(new FixedTimeProvider(now));
        CheckingWindowDto window = Window(
            new DateTime(2026, 7, 15, 9, 0, 0),
            new DateTime(2026, 7, 15, 12, 0, 0));

        Assert.False(service.IsOpen(window));
    }

    [Fact]
    public void OpenWindows_returns_only_windows_open_at_the_same_injected_time()
    {
        DateTimeOffset now = new(2026, 7, 15, 11, 30, 0, TimeSpan.Zero);
        WindowStatusService service = new(new FixedTimeProvider(now));
        CheckingWindowDto open = Window(
            new DateTime(2026, 7, 15, 12, 0, 0),
            new DateTime(2026, 7, 15, 13, 0, 0));
        CheckingWindowDto closed = Window(
            new DateTime(2026, 7, 15, 9, 0, 0),
            new DateTime(2026, 7, 15, 12, 0, 0));

        IReadOnlyList<CheckingWindowDto> result = service.OpenWindows([closed, open]);

        Assert.Equal([open], result);
    }

    private static CheckingWindowDto Window(DateTime startDate, DateTime endDate) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = "Checking window",
            StartDate = startDate,
            EndDate = endDate,
            KeyStage = KeyStages.KS2,
            CheckingWindowType = CheckingWindowType.KS2
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => LondonTimeZone;
    }
}
