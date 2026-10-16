using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

// The establishment-wide requests page: "open" and the deadline are
// asked of ICheckingExerciseService for the signed-in school, never of the window's stored dates.
// A school in an extension sees its own later end; every other school sees the exercises' own.
public sealed class UrnAmendmentRequestsServiceTests
{
    private static readonly Guid WindowId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime ExerciseEnd = new(2026, 10, 1, 17, 0, 0);
    private static readonly DateTime ExtensionEnd = new(2026, 10, 15, 17, 0, 0);

    private readonly IRequestRepository _requests = Substitute.For<IRequestRepository>();
    private readonly IWindowRepository _windows = Substitute.For<IWindowRepository>();
    private readonly ICurrentUserService _user = Substitute.For<ICurrentUserService>();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public UrnAmendmentRequestsServiceTests()
    {
        _user.OrganisationUrn.Returns("142313");
        _requests.GetAllSubmittedRequestsAsync(142313).Returns([]);
        _windows.GetAllWindowsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new CheckingWindowDto
            {
                Id = WindowId, Title = "KS4 June", KeyStage = KeyStages.KS4,
                CheckingWindowType = CheckingWindowType.KS4June,
                StartDate = new DateTime(2026, 9, 1), EndDate = ExerciseEnd,
                Exercises =
                [
                    new CheckingExerciseDto
                    {
                        Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData,
                        StartDate = new DateTime(2026, 9, 1), EndDate = ExerciseEnd, IsEnabled = true,
                        Extensions = [new CheckingExerciseExtensionDto { Id = Guid.NewGuid(), EndDate = ExtensionEnd, Laestabs = ["9334290"] }]
                    }
                ]
            }
        ]);
    }

    private UrnAmendmentRequestsService Sut() =>
        new(_requests, _windows, _user, new CheckingExerciseService(new FixedTimeProvider(Now)));

    [Fact]
    public async Task A_school_in_an_open_extension_sees_the_window_open_with_its_own_end()
    {
        _user.OrganisationLaestab.Returns("933/4290");

        var result = await Sut().GetAllSubmittedAmendmentRequestsAsync(CancellationToken.None);

        var window = Assert.Single(result.OpenWindows);
        Assert.Equal(WindowId, window.WindowId);
        Assert.Equal(ExtensionEnd, window.WindowEndDate);
    }

    [Fact]
    public async Task A_school_no_extension_names_sees_the_window_closed()
    {
        // The exercise ended on 1 Oct; only the named school has until the 15th.
        _user.OrganisationLaestab.Returns("860/4070");

        var result = await Sut().GetAllSubmittedAmendmentRequestsAsync(CancellationToken.None);

        Assert.Empty(result.OpenWindows);
    }

    [Fact]
    public async Task A_school_with_no_laestab_claim_sees_the_exercises_own_dates()
    {
        _user.OrganisationLaestab.Returns(string.Empty);

        var result = await Sut().GetAllSubmittedAmendmentRequestsAsync(CancellationToken.None);

        Assert.Empty(result.OpenWindows);
    }
}
