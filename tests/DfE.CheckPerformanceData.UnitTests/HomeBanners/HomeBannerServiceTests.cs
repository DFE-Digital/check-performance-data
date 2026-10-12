using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.HomeBanners;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.HomeBanners;

public sealed class HomeBannerServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0);
    private readonly IHomeBannerRepository _repo = Substitute.For<IHomeBannerRepository>();
    private readonly IHtmlRenderingService _html = Substitute.For<IHtmlRenderingService>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Now, TimeSpan.Zero));
    private readonly HomeBannerService _sut;

    public HomeBannerServiceTests()
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _sut = new HomeBannerService(_repo, _html, _clock);
        _repo.ExecuteInTransactionAsync(Arg.Any<Func<Task>>()).Returns(ci => ((Func<Task>)ci[0])());
        _html.RenderHtml(Arg.Any<string?>()).Returns(ci => "<clean>" + (string?)ci[0] + "</clean>");
    }

    private static HomeBannerDto Banner(int id, bool enabled = true, DateTime? from = null, DateTime? until = null, int order = 0, string heading = "H") =>
        new() { Id = id, Heading = heading, Body = "<p>b</p>", IsEnabled = enabled, ShowFrom = from, ShowUntil = until, SortOrder = order };

    [Fact]
    public async Task GetLiveAsync_ReturnsOnlyLiveBanners_InRepositoryOrder()
    {
        _repo.GetAllAsync().Returns(
        [
            Banner(1, order: 0),
            Banner(2, order: 1),
            Banner(3, enabled: false),
            Banner(4, from: Now.AddDays(1)),
            Banner(5, until: Now),
        ]);

        var live = await _sut.GetLiveAsync();

        Assert.Equal([1, 2], live.Select(b => b.Id));
        Assert.All(live, b => Assert.Equal(HomeBannerStatus.Live, b.Status));
        Assert.All(live, b => Assert.Equal("<clean><p>b</p></clean>", b.BodyHtml));
    }

    [Fact]
    public async Task GetAllAsync_SetsStatusOnEveryBanner()
    {
        _repo.GetAllAsync().Returns([Banner(1), Banner(2, enabled: false), Banner(3, from: Now.AddHours(1)), Banner(4, until: Now.AddHours(-1))]);

        var all = await _sut.GetAllAsync();

        Assert.Equal(
            [HomeBannerStatus.Live, HomeBannerStatus.Off, HomeBannerStatus.Scheduled, HomeBannerStatus.Expired],
            all.Select(b => b.Status));
    }

    [Fact]
    public async Task CreateAsync_SanitisesBody_AndRecordsVersionOne()
    {
        var content = new HomeBannerContent("New rules", "<p>x</p><script>1</script>", true, null, null);
        _repo.AddAsync(Arg.Any<HomeBannerContent>(), null, null).Returns(ci => Banner(9, heading: ((HomeBannerContent)ci[0]).Heading));

        var created = await _sut.CreateAsync(content);

        Assert.Equal(9, created.Id);
        await _repo.Received(1).AddAsync(Arg.Is<HomeBannerContent>(c => c.Body == "<clean><p>x</p><script>1</script></clean>" && c.Heading == "New rules"), null, null);
        await _repo.Received(1).AddVersionAsync(9, Arg.Is<HomeBannerContent>(c => c.Heading == "New rules"), 1);
    }

    [Fact]
    public async Task UpdateAsync_OverwritesAndAddsNextVersion()
    {
        _repo.GetByIdAsync(5).Returns(Banner(5));
        _repo.GetMaxVersionNumberAsync(5).Returns(3);
        var content = new HomeBannerContent("Edited", "<p>y</p>", false, Now, null);

        var updated = await _sut.UpdateAsync(5, content);

        Assert.NotNull(updated);
        await _repo.Received(1).UpdateAsync(5, Arg.Is<HomeBannerContent>(c => c.Heading == "Edited" && c.Body == "<clean><p>y</p></clean>" && !c.IsEnabled && c.ShowFrom == Now));
        await _repo.Received(1).AddVersionAsync(5, Arg.Is<HomeBannerContent>(c => c.Heading == "Edited"), 4);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull_AndWritesNothing()
    {
        _repo.GetByIdAsync(404).ReturnsNull();

        Assert.Null(await _sut.UpdateAsync(404, new HomeBannerContent("a", "b", true, null, null)));
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<int>(), Arg.Any<HomeBannerContent>());
    }

    [Fact]
    public async Task RestoreVersionAsync_CopiesTheVersion_AndRecordsItAsANewVersion()
    {
        _repo.GetByIdAsync(5).Returns(Banner(5));
        _repo.GetVersionByIdAsync(77).Returns(new HomeBannerVersionDto { Id = 77, HomeBannerId = 5, VersionNumber = 2, Heading = "Old", Body = "<p>old</p>", IsEnabled = true, ShowFrom = null, ShowUntil = Now.AddDays(3) });
        _repo.GetMaxVersionNumberAsync(5).Returns(6);

        var restored = await _sut.RestoreVersionAsync(5, 77);

        Assert.NotNull(restored);
        await _repo.Received(1).UpdateAsync(5, Arg.Is<HomeBannerContent>(c => c.Heading == "Old" && c.Body == "<p>old</p>" && c.ShowUntil == Now.AddDays(3)));
        await _repo.Received(1).AddVersionAsync(5, Arg.Is<HomeBannerContent>(c => c.Heading == "Old"), 7);
    }

    [Fact]
    public async Task RestoreVersionAsync_VersionOfAnotherBanner_ReturnsNull()
    {
        _repo.GetByIdAsync(5).Returns(Banner(5));
        _repo.GetVersionByIdAsync(77).Returns(new HomeBannerVersionDto { Id = 77, HomeBannerId = 6, VersionNumber = 1 });

        Assert.Null(await _sut.RestoreVersionAsync(5, 77));
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<int>(), Arg.Any<HomeBannerContent>());
    }

    [Fact]
    public async Task MoveAsync_Up_SwapsWithThePreviousBanner_AndRenumbersFromZero()
    {
        _repo.GetAllAsync().Returns([Banner(1, order: 0), Banner(2, order: 5), Banner(3, order: 9)]);

        IReadOnlyList<(int Id, int SortOrder)>? captured = null;
        await _repo.SetSortOrdersAsync(Arg.Do<IReadOnlyList<(int Id, int SortOrder)>>(o => captured = o));

        await _sut.MoveAsync(3, "up");

        Assert.Equal([(1, 0), (3, 1), (2, 2)], captured!);
    }

    [Fact]
    public async Task MoveAsync_DownAtTheEnd_IsANoOp()
    {
        _repo.GetAllAsync().Returns([Banner(1, order: 0), Banner(2, order: 1)]);

        await _sut.MoveAsync(2, "down");

        await _repo.DidNotReceive().SetSortOrdersAsync(Arg.Any<IReadOnlyList<(int Id, int SortOrder)>>());
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        _repo.GetByIdAsync(1).ReturnsNull();
        Assert.False(await _sut.DeleteAsync(1));
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task GetByIdAsync_ShowsCreatedAndUpdatedInUkTime()
    {
        // The repository stamps UTC; the admin pages say "UK time", so in BST 12:00 UTC must read 13:00.
        _clock.SetLocalTimeZone(UkZone());
        var summerUtc = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        _repo.GetByIdAsync(1).Returns(new HomeBannerDto { Id = 1, Heading = "H", Body = "b", CreatedAt = summerUtc, UpdatedAt = summerUtc });

        var banner = await _sut.GetByIdAsync(1);

        Assert.Equal(new DateTime(2026, 7, 1, 13, 0, 0), banner!.UpdatedAt);
        Assert.Equal(new DateTime(2026, 7, 1, 13, 0, 0), banner.CreatedAt);
    }

    [Fact]
    public async Task GetLiveAsync_ComparesShowFrom_WithUkTime_InSummer()
    {
        // 09:30 UTC on 1 July is 10:30 in the UK (BST). A banner from 10:30 UK is live now; one
        // from 10:31 is not. Comparing with UTC would wrongly hide the 10:30 banner for an hour.
        // A fresh clock, because FakeTimeProvider cannot move back from the class's October "now".
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(UkZone());
        var sut = new HomeBannerService(_repo, _html, clock);
        _repo.GetAllAsync().Returns(
        [
            Banner(1, from: new DateTime(2026, 7, 1, 10, 30, 0)),
            Banner(2, from: new DateTime(2026, 7, 1, 10, 31, 0)),
        ]);

        var live = await sut.GetLiveAsync();

        var only = Assert.Single(live);
        Assert.Equal(1, only.Id);
        Assert.Equal(HomeBannerStatus.Live, only.Status);
        var all = await sut.GetAllAsync();
        Assert.Equal(HomeBannerStatus.Scheduled, all.Single(b => b.Id == 2).Status);
    }

    // IANA id on Linux (and on Windows with ICU); the Windows id as a fallback.
    private static TimeZoneInfo UkZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/London"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"); }
    }
}
