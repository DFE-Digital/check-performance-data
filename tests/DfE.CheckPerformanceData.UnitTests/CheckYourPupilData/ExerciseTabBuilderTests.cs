using System.Text;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace DfE.CheckPerformanceData.UnitTests.CheckYourPupilData;

public sealed class ExerciseTabBuilderTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0);
    private readonly ICheckingDataReader _reader = Substitute.For<ICheckingDataReader>();

    private ExerciseTabBuilder Builder() => new(_reader, new ExerciseDisplayService(),
        new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)),
        NullLogger<ExerciseTabBuilder>.Instance);

    private static CheckingWindowDto Window(params CheckingExerciseDto[] exercises) => new()
    {
        Id = Guid.NewGuid(), Title = "t", StartDate = Now.AddDays(-1), EndDate = Now.AddDays(1),
        KeyStage = KeyStages.Post16, CheckingWindowType = CheckingWindowType.Post16,
        Exercises = [.. exercises]
    };

    // Datasets carries one complete slot by default so the tests that read a school's file also
    // reach the schema-reading step; AnExerciseWithNoSchemas turns it off explicitly.
    private static CheckingExerciseDto Exercise(string? tabName = "Students", bool enabled = true) => new()
    {
        Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData,
        StartDate = Now.AddDays(-1), EndDate = Now.AddDays(1),
        Name = "Student data", TabName = tabName, IsEnabled = enabled, UsesExerciseStorage = true,
        Datasets =
        [
            new CheckingWindowDatasetDto
            {
                Name = "data", IngressFile = "d.csv", IngressFileChecksum = "1",
                SchemaFile = "d.json", SchemaFileChecksum = "2"
            }
        ]
    };

    [Fact]
    public async Task AWindowWhoseExercisesDrawNoTabs_BuildsNoTabs()
    {
        // This is every window configured before #466. The page must fall back to its old tabs.
        var tabs = await Builder().BuildAsync(Window(Exercise(tabName: null)), "933/4290", null, null, 0, 10, default);

        Assert.Empty(tabs);
    }

    [Fact]
    public async Task AnExerciseWithNoDataForThisSchool_StillDrawsItsTab()
    {
        // The tab says there is nothing rather than vanishing, so a school can tell "not yours"
        // from "not published yet".
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var tab = Assert.Single(await Builder().BuildAsync(Window(Exercise()), "933/4290", null, null, 0, 10, default));

        Assert.False(tab.HasData);
        Assert.Empty(tab.Rows);
    }

    [Fact]
    public async Task AnExerciseWithNoSchemas_RendersARawTable()
    {
        var exercise = Exercise();
        exercise.Datasets = [];
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""[{"ULN":"1","SURNAME":"Smith"}]"""));

        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, null, 0, 10, default));

        Assert.Null(tab.Table);
        Assert.Null(tab.Vertical);
        Assert.Equal(["ULN", "SURNAME"], tab.Columns);
    }

    [Fact]
    public async Task AMissingSchema_IsReportedRatherThanGuessedAround()
    {
        var exercise = Exercise();
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""[{"ULN":"1"}]"""));
        _reader.ReadSchemaAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, null, 0, 10, default));

        Assert.True(tab.SchemaUnavailable);
    }

    [Fact]
    public async Task ASchemaAskingForAVerticalLayout_GetsOne()
    {
        var exercise = Exercise();
        exercise.Datasets =
        [
            new CheckingWindowDatasetDto
            {
                Name = "summary", IngressFile = "s.csv", IngressFileChecksum = "1",
                SchemaFile = "s.json", SchemaFileChecksum = "2"
            }
        ];
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""[{"ULN":"1"}]"""));
        _reader.ReadSchemaAsync(Arg.Any<Guid>(), "s.json", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""
                {"x-display":{"layout":"vertical"},"properties":{"ULN":{"x-display":{"label":"ULN"}}}}
                """));

        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, null, 0, 10, default));

        Assert.NotNull(tab.Vertical);
        Assert.Null(tab.Table);
        Assert.Equal("ULN", Assert.Single(tab.Vertical!.Fields).Label);
    }

    [Fact]
    public async Task AnExerciseMixingVerticalAndTableSchemas_RendersATable()
    {
        // A mix is a schema-authoring fault; the table renderer can show every dataset, so it
        // wins rather than silently dropping the odd one out. Only a warning is logged.
        var exercise = Exercise();
        exercise.Datasets =
        [
            new CheckingWindowDatasetDto
            {
                Name = "summary", IngressFile = "s.csv", IngressFileChecksum = "1",
                SchemaFile = "s.json", SchemaFileChecksum = "2", SortOrder = 0
            },
            new CheckingWindowDatasetDto
            {
                Name = "students", IngressFile = "t.csv", IngressFileChecksum = "3",
                SchemaFile = "t.json", SchemaFileChecksum = "4", SortOrder = 1
            }
        ];
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""[{"ULN":"1","SURNAME":"Smith"}]"""));
        _reader.ReadSchemaAsync(Arg.Any<Guid>(), "s.json", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""
                {"x-display":{"layout":"vertical"},"properties":{"ULN":{"x-display":{"label":"ULN"}}}}
                """));
        _reader.ReadSchemaAsync(Arg.Any<Guid>(), "t.json", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""
                {"properties":{"SURNAME":{"x-display":{"label":"Surname"}}}}
                """));

        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, null, 0, 10, default));

        Assert.NotNull(tab.Table);
        Assert.Null(tab.Vertical);
    }

    [Fact]
    public async Task TabsComeBackInTabOrder()
    {
        // TabOrder and Name are init-only on CheckingExerciseDto, so the two exercises are built
        // fully-formed (out of tab order) rather than mutated after construction.
        var second = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData,
            StartDate = Now.AddDays(-1), EndDate = Now.AddDays(1),
            Name = "Second", TabName = "Students", TabOrder = 1, IsEnabled = true, UsesExerciseStorage = true
        };
        var first = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData,
            StartDate = Now.AddDays(-1), EndDate = Now.AddDays(1),
            Name = "First", TabName = "Students", TabOrder = 0, IsEnabled = true, UsesExerciseStorage = true
        };
        var window = Window(second, first);
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var tabs = await Builder().BuildAsync(window, "933/4290", null, null, 0, 10, default);

        Assert.Equal(["First", "Second"], tabs.Select(t => t.Exercise.Name));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
