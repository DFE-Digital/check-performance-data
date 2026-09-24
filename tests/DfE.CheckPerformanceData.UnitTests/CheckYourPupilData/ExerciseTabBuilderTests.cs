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
    private static CheckingExerciseDto Exercise(string tabName = "Students", bool enabled = true,
        ExerciseLayout layout = ExerciseLayout.Table) => new()
    {
        Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData, Layout = layout,
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
    public async Task AWindowWithNoVisibleExercise_BuildsNoTabs()
    {
        var tabs = await Builder().BuildAsync(Window(Exercise(enabled: false)), "933/4290", null, null, null, 0, 10, default);

        Assert.Empty(tabs);
    }

    [Fact]
    public async Task AnExerciseWithNoDataForThisSchool_StillDrawsItsTab()
    {
        // The tab says there is nothing rather than vanishing, so a school can tell "not yours"
        // from "not published yet".
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var tab = Assert.Single(await Builder().BuildAsync(Window(Exercise()), "933/4290", null, null, null, 0, 10, default));

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

        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, null, null, 0, 10, default));

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

        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, null, null, 0, 10, default));

        Assert.True(tab.SchemaUnavailable);
    }

    [Fact]
    public async Task AnExerciseSetToASummaryList_IsShownVertically()
    {
        var exercise = Exercise(layout: ExerciseLayout.Vertical);
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
            .Returns(Encoding.UTF8.GetBytes("""{"properties":{"ULN":{"x-display":{"label":"ULN"}}}}"""));

        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, null, null, 0, 10, default));

        Assert.NotNull(tab.Vertical);
        Assert.Null(tab.Table);
        Assert.Equal("ULN", Assert.Single(tab.Vertical!.Fields).Label);
    }

    [Fact]
    public async Task ASchemaAskingForAVerticalLayout_IsIgnored_TheExerciseDecides()
    {
        // The admin sets the layout on the exercise. A schema cannot override it.
        var exercise = Exercise(layout: ExerciseLayout.Table);
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

        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, null, null, 0, 10, default));

        Assert.NotNull(tab.Table);
        Assert.Null(tab.Vertical);
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

        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, null, null, 0, 10, default));

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

        var tabs = await Builder().BuildAsync(window, "933/4290", null, null, null, 0, 10, default);

        Assert.Equal(["First", "Second"], tabs.Select(t => t.Exercise.Name));
    }

    // KS4 sends one pupils file, and each record carries its own P_INCL. 401 is an included code,
    // 402 is not, and a record with no P_INCL is not included.
    private CheckingExerciseDto AnInclusionTabsExercise()
    {
        var exercise = Exercise(tabName: "Pupils", layout: ExerciseLayout.InclusionTabs);
        exercise.Datasets =
        [
            new CheckingWindowDatasetDto
            {
                Name = "pupils", IngressFile = "p.csv", IngressFileChecksum = "1",
                SchemaFile = "p.json", SchemaFileChecksum = "2"
            }
        ];
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""
                [{"SURNAME":"Adams","P_INCL":"401"},{"SURNAME":"Brown","P_INCL":402},
                 {"SURNAME":"Clark"},{"SURNAME":"Davis","P_INCL":"431"}]
                """));
        _reader.ReadSchemaAsync(Arg.Any<Guid>(), "p.json", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""
                {"x-ingress":{"collection":"pupils"},"x-download":{"fileName":"ks4pupils.csv","label":"Pupils"},
                 "properties":{"SURNAME":{"x-display":{"label":"Surname","searchable":true}}}}
                """));
        return exercise;
    }

    [Fact]
    public async Task AnInclusionTabsExercise_DrawsAnIncludedTabAndANonIncludedTab()
    {
        var exercise = AnInclusionTabsExercise();

        var tabs = await Builder().BuildAsync(Window(exercise), "933/4290", null, null, null, 0, 10, default);

        Assert.Equal(["Pupils Included", "Pupils Non Included"], tabs.Select(t => t.Label));
        Assert.Equal([$"exercise-{exercise.Id}-included", $"exercise-{exercise.Id}-nonincluded"],
            tabs.Select(t => t.Key));
        Assert.Equal(["Adams", "Davis"], tabs[0].Table!.Rows.Select(r => r["SURNAME"]));
        Assert.Equal(["Brown", "Clark"], tabs[1].Table!.Rows.Select(r => r["SURNAME"]));
    }

    [Fact]
    public async Task AnInclusionTab_DownloadsOnlyItsOwnPupils_UnderItsOwnFileName()
    {
        // The download-all zip holds every tab's CSV, so the two must not share a file name.
        var tabs = await Builder().BuildAsync(Window(AnInclusionTabsExercise()), "933/4290", null, null, null, 0, 10, default);

        var included = Assert.Single(tabs[0].Table!.Datasets);
        var nonIncluded = Assert.Single(tabs[1].Table!.Datasets);
        Assert.Equal(2, included.Rows.Count);
        Assert.Equal(2, nonIncluded.Rows.Count);
        Assert.NotEqual(included.FileName, nonIncluded.FileName);
        Assert.Equal("Pupils Included", included.Label);
    }

    [Fact]
    public async Task AnInclusionTab_KeepsThePupilCsvNamesAndOrder()
    {
        // The names and order the pupil CSVs had before exercises. The controller adds the
        // school, window type and year.
        var exercise = AnInclusionTabsExercise();
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""
                [{"SURNAME":"Smith","FORENAME":"Bob","P_INCL":"401"},
                 {"SURNAME":"Adams","FORENAME":"Zoe","P_INCL":"401"},
                 {"SURNAME":"Smith","FORENAME":"Amy","P_INCL":"401"}]
                """));

        var tabs = await Builder().BuildAsync(Window(exercise), "933/4290", null, null, null, 0, 10, default);

        Assert.Equal("pupil-include.csv", tabs[0].Table!.Datasets[0].FileName);
        Assert.Equal("pupil-non-include.csv", tabs[1].Table!.Datasets[0].FileName);
        Assert.Equal(["Adams Zoe", "Smith Amy", "Smith Bob"],
            tabs[0].Table!.Datasets[0].Rows.Select(r => $"{r["SURNAME"]} {r["FORENAME"]}"));
    }

    [Fact]
    public async Task AnIncludedStamp_DecidesInclusionBeforePIncl()
    {
        // A file stamped by its slot (INCLUDED) was placed by the admin; the stamp wins.
        var exercise = AnInclusionTabsExercise();
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""[{"SURNAME":"Adams","P_INCL":"402","INCLUDED":true}]"""));

        var tabs = await Builder().BuildAsync(Window(exercise), "933/4290", null, null, null, 0, 10, default);

        Assert.Single(tabs[0].Table!.Rows);
        Assert.Empty(tabs[1].Table!.Rows);
    }

    [Fact]
    public async Task ASearch_AppliesOnlyToTheTabItWasMadeOn()
    {
        var exercise = AnInclusionTabsExercise();

        var tabs = await Builder().BuildAsync(Window(exercise), "933/4290",
            $"exercise-{exercise.Id}-nonincluded", null, "Clark", 0, 10, default);

        Assert.Equal(["Adams", "Davis"], tabs[0].Table!.Rows.Select(r => r["SURNAME"]));
        Assert.Null(tabs[0].Table!.Search);
        Assert.Equal(["Clark"], tabs[1].Table!.Rows.Select(r => r["SURNAME"]));
    }

    [Fact]
    public async Task AnInclusionTabsExerciseWithNoDataForThisSchool_StillDrawsBothTabs()
    {
        var exercise = AnInclusionTabsExercise();
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var tabs = await Builder().BuildAsync(Window(exercise), "933/4290", null, null, null, 0, 10, default);

        Assert.Equal(["Pupils Included", "Pupils Non Included"], tabs.Select(t => t.Label));
        Assert.All(tabs, t => Assert.False(t.HasData));
    }

    [Theory]
    [InlineData(CheckingWindowType.KS4June, "pupil")]
    [InlineData(CheckingWindowType.Post16, "student")]
    public async Task EveryTab_CarriesItsWindowsLearnerNoun(CheckingWindowType type, string noun)
    {
        // The table partial is shared by every key stage, so its wording comes from the tab.
        var window = Window(AnInclusionTabsExercise());
        window.CheckingWindowType = type;

        var tabs = await Builder().BuildAsync(window, "933/4290", null, null, null, 0, 10, default);
        var noLaestab = await Builder().BuildAsync(window, null, null, null, null, 0, 10, default);

        Assert.All(tabs.Concat(noLaestab), t => Assert.Equal(noun, t.LearnerNoun.Singular));
    }

    [Fact]
    public async Task ATableExercise_KeepsOneTabNamedByItsTabName()
    {
        var exercise = Exercise(tabName: "Students");
        _reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "933/4290", Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, null, null, 0, 10, default));

        Assert.Equal("Students", tab.Label);
        Assert.Equal($"exercise-{exercise.Id}", tab.Key);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static CheckingExerciseDto WithRelease(CheckingExerciseDto exercise, Guid releaseId,
        params CheckingExerciseReleaseFileDto[] files) => new()
    {
        Id = exercise.Id, ExerciseType = exercise.ExerciseType, StartDate = exercise.StartDate,
        EndDate = exercise.EndDate, Name = exercise.Name, TabName = exercise.TabName, Layout = exercise.Layout,
        IsEnabled = true, UsesExerciseStorage = true, Datasets = exercise.Datasets,
        CurrentReleaseId = releaseId,
        Releases = [new CheckingExerciseReleaseDto { Id = releaseId, Number = 1, Files = [.. files] }]
    };

    [Fact]
    public async Task AnExerciseWithARelease_ReadsThatReleasesDataAndSchema_NotANewerSlotUpload()
    {
        // The admin has uploaded the revised file and its schema (with its new title) but has not
        // run them. Schools keep seeing the live release, under its own title.
        var releaseId = Guid.NewGuid();
        var datasetId = Guid.NewGuid();
        var exercise = Exercise(layout: ExerciseLayout.Vertical);
        exercise.Datasets =
        [
            new CheckingWindowDatasetDto
            {
                Id = datasetId, Name = "summary", IngressFile = "new.csv", IngressFileChecksum = "3",
                SchemaFile = "new.json", SchemaFileChecksum = "4"
            }
        ];
        var withRelease = WithRelease(exercise, releaseId, new CheckingExerciseReleaseFileDto
        {
            DatasetId = datasetId, DatasetName = "summary", IngressFile = "old.csv", IngressFileChecksum = "1",
            SchemaFile = "old.json", SchemaFileChecksum = "2"
        });
        _reader.ReadDatasetAsync(Arg.Is<CheckingDataExercise>(e => e.CurrentReleaseId == releaseId), datasetId,
                "933/4290", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""[{"ULN":"1"}]"""));
        _reader.ReadSchemaAsync(Arg.Any<Guid>(), "old.json", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""{"properties":{"ULN":{"x-display":{"label":"Old label"}}}}"""));
        _reader.ReadSchemaAsync(Arg.Any<Guid>(), "new.json", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""{"properties":{"ULN":{"x-display":{"label":"New label"}}}}"""));

        var tab = Assert.Single(await Builder().BuildAsync(Window(withRelease), "933/4290", null, null, null, 0, 10, default));

        Assert.True(tab.HasData);
        Assert.Equal("Old label", Assert.Single(tab.Vertical!.Fields).Label);
        await _reader.DidNotReceive().ReadSchemaAsync(Arg.Any<Guid>(), "new.json", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DatasetsWithUnrelatedSchemas_EachShowOnlyTheirOwnRows()
    {
        // Two data shares on one exercise with nothing in common: no INCLUDED flag, and field
        // names that do not tell them apart. Each has its own file, so no row can land under the
        // wrong dataset or be dropped.
        var releaseId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var exercise = WithRelease(Exercise(), releaseId,
            new CheckingExerciseReleaseFileDto { DatasetId = first, DatasetName = "a", SchemaFile = "a.json", SortOrder = 0 },
            new CheckingExerciseReleaseFileDto { DatasetId = second, DatasetName = "b", SchemaFile = "b.json", SortOrder = 1 });
        _reader.ReadDatasetAsync(Arg.Any<CheckingDataExercise>(), first, "933/4290", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""[{"ULN":"1","NAME":"Ann"}]"""));
        _reader.ReadDatasetAsync(Arg.Any<CheckingDataExercise>(), second, "933/4290", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""[{"ULN":"2","NAME":"Ben"},{"ULN":"3","NAME":"Cat"}]"""));
        _reader.ReadSchemaAsync(Arg.Any<Guid>(), "a.json", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""{"x-ingress":{"collection":"share-a"},"properties":{"ULN":{},"NAME":{}}}"""));
        _reader.ReadSchemaAsync(Arg.Any<Guid>(), "b.json", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""{"x-ingress":{"collection":"share-b"},"properties":{"ULN":{},"NAME":{}}}"""));

        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, "share-b", null, 0, 10, default));

        var datasets = tab.Table!.Datasets.ToDictionary(d => d.Key);
        Assert.Equal(["1"], datasets["share-a"].Rows.Select(r => r["ULN"]));
        Assert.Equal(["2", "3"], datasets["share-b"].Rows.Select(r => r["ULN"]));
        await _reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default!, default);
    }

    [Fact]
    public async Task AReleaseFromBeforePerDatasetFiles_IsReadFromItsMergedFile()
    {
        // Such a release records no dataset ids: it wrote only the merged file.
        var releaseId = Guid.NewGuid();
        var exercise = WithRelease(Exercise(), releaseId,
            new CheckingExerciseReleaseFileDto { DatasetName = "data", SchemaFile = "d.json" });
        _reader.ReadAsync(Arg.Is<CheckingDataExercise>(e => e.CurrentReleaseId == releaseId), "933/4290",
                Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""[{"ULN":"1"}]"""));
        _reader.ReadSchemaAsync(Arg.Any<Guid>(), "d.json", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""{"properties":{"ULN":{}}}"""));

        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, null, null, 0, 10, default));

        Assert.True(tab.HasData);
        await _reader.DidNotReceiveWithAnyArgs().ReadDatasetAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task TheRawDownloadOfARelease_HoldsEachDatasetUnderItsName()
    {
        var releaseId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var exercise = WithRelease(Exercise(), releaseId,
            new CheckingExerciseReleaseFileDto { DatasetId = first, DatasetName = "a", SchemaFile = "a.json" });
        _reader.ReadDatasetAsync(Arg.Any<CheckingDataExercise>(), first, "933/4290", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""[{"ULN":"1"}]"""));
        _reader.ReadSchemaAsync(Arg.Any<Guid>(), "a.json", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("""{"properties":{"ULN":{}}}"""));
        var tab = Assert.Single(await Builder().BuildAsync(Window(exercise), "933/4290", null, null, null, 0, 10, default));

        var raw = await Builder().ReadRawAsync(tab, "933/4290", default);

        using var json = System.Text.Json.JsonDocument.Parse(raw!);
        Assert.Equal("1", json.RootElement.GetProperty("a")[0].GetProperty("ULN").GetString());
    }
}
