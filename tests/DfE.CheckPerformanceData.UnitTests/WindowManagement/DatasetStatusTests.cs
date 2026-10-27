using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// The Data tab used to say "In use: Yes" for every slot that was not retired, so an empty optional
// slot (included revised with retention, before March) read as if schools saw it. The status says
// whether schools see the files the slot holds now.
public sealed class DatasetStatusTests
{
    private static readonly Guid DatasetId = Guid.NewGuid();

    private static CheckingWindowDatasetDto Slot(string ingress = "c1", string schema = "s1", bool retired = false) => new()
    {
        Id = DatasetId, Name = "16to19_INC_REV", Retired = retired,
        IngressFile = ingress == "" ? "" : "file.csv", IngressFileChecksum = ingress,
        SchemaFile = schema == "" ? "" : "schema.json", SchemaFileChecksum = schema
    };

    private static CheckingExerciseDto Exercise(bool released)
    {
        var release = new CheckingExerciseReleaseDto
        {
            Id = Guid.NewGuid(),
            Files =
            [
                new CheckingExerciseReleaseFileDto
                {
                    DatasetId = DatasetId, DatasetName = "16to19_INC_REV",
                    IngressFileChecksum = "c1", SchemaFileChecksum = "s1"
                }
            ]
        };
        return new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.ResultsEnquiry,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 6, 1),
            CurrentReleaseId = released ? release.Id : null,
            Releases = released ? [release] : []
        };
    }

    [Fact]
    public void An_empty_slot_is_not_supplied()
        => Assert.Equal(DatasetStatus.NotSupplied, Exercise(released: true).StatusOf(Slot("", "")));

    [Fact]
    public void A_slot_with_a_csv_but_no_schema_is_not_supplied()
        => Assert.Equal(DatasetStatus.NotSupplied, Exercise(released: true).StatusOf(Slot(schema: "")));

    [Fact]
    public void A_slot_the_live_release_read_is_live()
        => Assert.Equal(DatasetStatus.Live, Exercise(released: true).StatusOf(Slot()));

    [Theory]
    [InlineData("c2", "s1")]
    [InlineData("c1", "s2")]
    public void A_slot_whose_file_changed_after_the_run_is_not_validated(string ingress, string schema)
        => Assert.Equal(DatasetStatus.NotValidated, Exercise(released: true).StatusOf(Slot(ingress, schema)));

    [Fact]
    public void A_complete_slot_of_an_exercise_with_no_release_is_not_validated()
        => Assert.Equal(DatasetStatus.NotValidated, Exercise(released: false).StatusOf(Slot()));

    [Fact]
    public void A_retired_slot_is_retired_even_while_the_live_release_still_reads_it()
        => Assert.Equal(DatasetStatus.Retired, Exercise(released: true).StatusOf(Slot(retired: true)));

    [Fact]
    public void Every_status_has_its_own_tag()
    {
        var statuses = Enum.GetValues<DatasetStatus>();

        Assert.Equal(statuses.Length, statuses.Select(DatasetStatusTags.Label).Distinct().Count());
        Assert.Equal(statuses.Length, statuses.Select(DatasetStatusTags.CssClass).Distinct().Count());
        Assert.Equal("Live", DatasetStatusTags.Label(DatasetStatus.Live));
    }
}
