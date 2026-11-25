using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// #937: the rules for adding a school to the sample data list of a window.
public sealed class SampleSchoolServiceTests
{
    private static readonly Guid WindowId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 30, 0, TimeSpan.Zero);

    private readonly ISampleSchoolRepository _repository = Substitute.For<ISampleSchoolRepository>();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public SampleSchoolServiceTests()
    {
        _repository.WindowExistsAsync(WindowId, Arg.Any<CancellationToken>()).Returns(true);
        _repository.ListAsync(WindowId, Arg.Any<CancellationToken>()).Returns([]);
        _repository.AddAsync(Arg.Any<NewSampleSchool>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private SampleSchoolService Sut() => new(_repository, new FixedTimeProvider(Now));

    [Theory]
    [InlineData("933/4290")]
    [InlineData("9334290")]
    [InlineData(" 933/4290 ")]
    public async Task Stores_the_laestab_as_digits_with_the_admin_and_the_time(string laestab)
    {
        var result = await Sut().AddAsync(WindowId, laestab, "123456", "Ada Admin", CancellationToken.None);

        Assert.Equal(SampleSchoolAddStatus.Ok, result.Status);
        await _repository.Received(1).AddAsync(
            new NewSampleSchool(WindowId, "9334290", "123456", Now.UtcDateTime, "Ada Admin"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "123456", SampleSchoolInput.LaestabMissing, null)]
    [InlineData("93342", "123456", SampleSchoolInput.LaestabInvalid, null)]
    [InlineData("933-4290", "123456", SampleSchoolInput.LaestabInvalid, null)]
    [InlineData("9334290", "", null, SampleSchoolInput.UrnMissing)]
    [InlineData("9334290", "12345A", null, SampleSchoolInput.UrnInvalid)]
    [InlineData("9334290", "1234567", null, SampleSchoolInput.UrnInvalid)]
    [InlineData("", "", SampleSchoolInput.LaestabMissing, SampleSchoolInput.UrnMissing)]
    public async Task Refuses_a_value_that_is_not_valid_and_writes_nothing(
        string? laestab, string? urn, string? laestabError, string? urnError)
    {
        var result = await Sut().AddAsync(WindowId, laestab, urn, "Ada Admin", CancellationToken.None);

        Assert.Equal(SampleSchoolAddStatus.Invalid, result.Status);
        Assert.Equal(laestabError, result.Input.LaestabError);
        Assert.Equal(urnError, result.Input.UrnError);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Refuses_a_school_that_the_window_already_lists()
    {
        _repository.ListAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns([new SampleSchoolDto(Guid.NewGuid(), WindowId, "9334290", "123456", Now.UtcDateTime, "Ada Admin")]);

        var result = await Sut().AddAsync(WindowId, "933/4290", "654321", "Ada Admin", CancellationToken.None);

        Assert.Equal(SampleSchoolAddStatus.AlreadyListed, result.Status);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Reports_already_listed_when_another_admin_adds_the_school_first()
    {
        _repository.AddAsync(Arg.Any<NewSampleSchool>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Sut().AddAsync(WindowId, "9334290", "123456", "Ada Admin", CancellationToken.None);

        Assert.Equal(SampleSchoolAddStatus.AlreadyListed, result.Status);
    }

    [Fact]
    public async Task Refuses_an_unknown_window()
    {
        var unknown = Guid.NewGuid();

        var result = await Sut().AddAsync(unknown, "9334290", "123456", "Ada Admin", CancellationToken.None);

        Assert.Equal(SampleSchoolAddStatus.WindowNotFound, result.Status);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task A_check_writes_nothing()
    {
        var result = await Sut().CheckAddAsync(WindowId, "9334290", "123456", CancellationToken.None);

        Assert.Equal(SampleSchoolAddStatus.Ok, result.Status);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Finds_a_school_only_in_its_own_window()
    {
        var id = Guid.NewGuid();
        _repository.ListAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns([new SampleSchoolDto(id, WindowId, "9334290", "123456", Now.UtcDateTime, "Ada Admin")]);

        Assert.NotNull(await Sut().FindAsync(WindowId, id, CancellationToken.None));
        Assert.Null(await Sut().FindAsync(Guid.NewGuid(), id, CancellationToken.None));
    }
}
