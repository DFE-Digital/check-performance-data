using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// AB#301022: the window details page says, per exercise, whether it is open — and so which of
// Close and "Send requests for processing" it may offer. The answer comes from the one clock
// (the real CheckingExerciseService on a fixed TimeProvider), never from the controller.
public sealed class SummaryControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTime LastMonth = new(2026, 8, 1);
    private static readonly DateTime Yesterday = new(2026, 9, 2, 17, 0, 0);
    private static readonly DateTime NextMonth = new(2026, 10, 1);
    private static readonly DateTime NextYear = new(2027, 8, 2, 17, 0, 0);

    private readonly IWindowService _windowService = Substitute.For<IWindowService>();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private SummaryController Sut() =>
        new(_windowService, new CheckingExerciseService(new FixedTimeProvider(Now)));

    private void TheWindowHolds(params CheckingExerciseDto[] exercises) =>
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "Key Stage 4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = exercises.Min(e => e.StartDate),
            EndDate = exercises.Max(e => e.EndDate),
            Exercises = [.. exercises]
        });

    private static CheckingExerciseDto Row(CheckingExerciseType type, DateTime start, DateTime end) => new()
    {
        Id = Guid.NewGuid(),
        ExerciseType = type,
        StartDate = start,
        EndDate = end,
        SortOrder = (int)type
    };

    private async Task<ExerciseSummarySection> SectionAsync(CheckingExerciseType type)
    {
        var view = Assert.IsType<ViewResult>(await Sut().Index(WindowId, CancellationToken.None));
        var model = Assert.IsType<WindowEditItem>(view.Model);
        return model.Exercises.Single(e => e.ExerciseType == type);
    }

    [Fact]
    public async Task Index_returns_not_found_for_an_unknown_window()
    {
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns((CheckingWindowDto?)null);

        Assert.IsType<NotFoundResult>(await Sut().Index(WindowId, CancellationToken.None));
    }

    [Fact]
    public async Task An_open_exercise_is_open_and_may_be_closed()
    {
        // AC1.
        TheWindowHolds(Row(CheckingExerciseType.PupilData, LastMonth, NextYear));

        var section = await SectionAsync(CheckingExerciseType.PupilData);

        Assert.True(section.IsOpen);
        Assert.False(section.HasClosed);
        Assert.Equal("Open", section.StatusLabel);
        Assert.Equal("govuk-tag--green", section.StatusTagClass);
        Assert.Equal($"/admin/windows/{WindowId}/PupilData/close", section.CloseLink);
    }

    [Fact]
    public async Task An_ended_exercise_is_closed_and_may_only_have_its_requests_sent()
    {
        // AC2 — and each exercise answers for itself: results enquiry is still open beside it.
        TheWindowHolds(
            Row(CheckingExerciseType.PupilData, LastMonth, Yesterday),
            Row(CheckingExerciseType.ResultsEnquiry, LastMonth, NextYear));

        var closed = await SectionAsync(CheckingExerciseType.PupilData);
        var open = await SectionAsync(CheckingExerciseType.ResultsEnquiry);

        Assert.False(closed.IsOpen);
        Assert.True(closed.HasClosed);
        Assert.Equal("Closed", closed.StatusLabel);
        Assert.Equal("govuk-tag--grey", closed.StatusTagClass);
        Assert.Equal($"/admin/windows/{WindowId}/PupilData/send-requests", closed.SendRequestsLink);
        Assert.True(open.IsOpen);
        Assert.Equal("Open", open.StatusLabel);
    }

    [Fact]
    public async Task An_exercise_that_has_not_started_is_upcoming_and_offers_neither()
    {
        // Not open is not the same as closed: there is nothing to close and nothing to send yet.
        TheWindowHolds(Row(CheckingExerciseType.PupilData, NextMonth, NextYear));

        var section = await SectionAsync(CheckingExerciseType.PupilData);

        Assert.False(section.IsOpen);
        Assert.False(section.HasClosed);
        Assert.Equal("Upcoming", section.StatusLabel);
        Assert.Equal("govuk-tag--blue", section.StatusTagClass);
    }
}
