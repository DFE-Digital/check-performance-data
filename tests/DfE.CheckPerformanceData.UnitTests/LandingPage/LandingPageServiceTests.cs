using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.LandingPage;

public class LandingPageServiceTests
{
    private readonly ILandingPageRepository _repository = Substitute.For<ILandingPageRepository>();
    private readonly IDfESignInApiClient _dfESignInApiClient = Substitute.For<IDfESignInApiClient>();
    private readonly ICurrentUserService _currentUserService = Substitute.For<ICurrentUserService>();
    private readonly LandingPageService _sut;

    private static readonly DateTimeOffset Now = new(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);

    public LandingPageServiceTests()
    {
        _currentUserService.UserId.Returns("user-1");
        _currentUserService.OrganisationId.Returns("org-1");
        _sut = new LandingPageService(_repository, new FakeTimeProvider(Now), _dfESignInApiClient, _currentUserService,
            Substitute.For<ILogger<LandingPageService>>());
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task WhenOrganisationNotFound_ReturnsNull()
    {
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").ReturnsNull();

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WhenOrganisationIdMissing_ReturnsNullWithoutCallingDfeApi(string organisationId)
    {
        // Synthetic principals (dev impersonation, plus any future code path that
        // doesn't carry the DfE Sign-In organisation claim) reach this method with an
        // empty OrganisationId. Hitting the DfE Sign-In API with an empty id 500s on
        // the upstream side and bubbles back as an unhandled exception. Guard explicitly.
        _currentUserService.OrganisationId.Returns(organisationId);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.Null(result);
        await _dfESignInApiClient.DidNotReceive().GetOrganisationAsync(
            Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task WhenWindowHasPupilDataAndMatchingKeyStage_IncludedInOpenWindows()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 16);
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var window = MakeWindow(keyStage: KeyStages.KS2, hasPupilData: true);
        _repository.GetOpenWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([window]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.OpenWindows);
        Assert.Equal(window.Id, result.OpenWindows[0].Id);
    }

    [Fact]
    public async Task WhenWindowHasNoPupilData_ExcludedFromOpenWindows_AddedToNoDataWindowsText()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 16);
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var window = MakeWindow(title: "KS2 2026", keyStage: KeyStages.KS2, hasPupilData: false);
        _repository.GetOpenWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([window]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.OpenWindows);
        Assert.Equal("KS2 2026", result.NoDataWindowsText);
        Assert.Null(result.NotValidWindowsText);
    }

    [Fact]
    public async Task WhenWindowKeyStageDoesNotMatchOrganisation_ExcludedFromOpenWindows_AddedToNotValidWindowsText()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 12); // KS2 only
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var window = MakeWindow(title: "KS4 2026", keyStage: KeyStages.KS4, hasPupilData: true);
        _repository.GetOpenWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([window]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.OpenWindows);
        Assert.Equal("KS4 2026", result.NotValidWindowsText);
        Assert.Null(result.NoDataWindowsText);
    }

    [Fact]
    public async Task WhenWindowHasNoDataAndWrongKeyStage_OnlyAppearsInNotValidWindowsText()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 12); // KS2 only
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var window = MakeWindow(title: "KS4 2026", keyStage: KeyStages.KS4, hasPupilData: false);
        _repository.GetOpenWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([window]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.OpenWindows);
        Assert.Equal("KS4 2026", result.NotValidWindowsText);
        Assert.Null(result.NoDataWindowsText);
    }

    [Fact]
    public async Task WhenNoExcludedWindows_NoDataWindowsTextAndNotValidWindowsTextAreNull()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 16);
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);

        var window = MakeWindow(keyStage: KeyStages.KS2, hasPupilData: true);
        _repository.GetOpenWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([window]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.NoDataWindowsText);
        Assert.Null(result.NotValidWindowsText);
    }

    [Fact]
    public async Task MapsOrganisationFieldsCorrectly()
    {
        var org = MakeOrganisation(lowAge: 3, highAge: 16, name: "Test School", laestab: "1234567",
            urn: "123456", address: "1 School Lane");
        _dfESignInApiClient.GetOrganisationAsync("user-1", "org-1").Returns(org);
        _repository.GetOpenWindowsAsync(Now.DateTime, org.Laestab, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _sut.GetLandingPageDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Test School", result.OrganisationName);
        Assert.Equal("1234567", result.OrganisationLaestab);
        Assert.Equal("123456", result.OrganisationUrn);
        Assert.Equal("1 School Lane", result.OrganisationAddress);
        Assert.Equal(org.KeyStages, result.KeyStages);
    }

    private static OrganisationDto MakeOrganisation(
        int lowAge = 3,
        int highAge = 16,
        string name = "Test School",
        string laestab = "8604070",
        string urn = "100000",
        string address = "1 Test Street") => new()
    {
        Id = "org-1",
        Urn = urn,
        Name = name,
        Laestab = laestab,
        Address = address,
        StatutoryLowAge = lowAge,
        StatutoryHighAge = highAge
    };

    private static CheckingWindowDto MakeWindow(
        string title = "Test Window",
        KeyStages keyStage = KeyStages.KS2,
        bool hasPupilData = true) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        KeyStage = keyStage,
        CheckingWindowType = CheckingWindowType.KS2,
        HasPupilData = hasPupilData,
        StartDate = DateTime.UtcNow.AddDays(-1),
        EndDate = DateTime.UtcNow.AddDays(30)
    };
}
