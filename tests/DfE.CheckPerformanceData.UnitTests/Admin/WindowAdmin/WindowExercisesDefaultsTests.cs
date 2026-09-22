using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #319 / #466: which exercises a window type starts with. Templates, not enum members, because a
// 16-19 window starts with a display-only Summary data share that no enum member describes. A
// starting point rather than a rule — the admin may untick any of them and add their own.
public class WindowExercisesDefaultsTests
{
    [Fact]
    public void Post16_starts_with_pupil_data_results_enquiry_and_summary_data()
    {
        var templates = WindowExercises.DefaultsFor(CheckingWindowType.Post16);

        Assert.Equal(
            ["Pupil data checking", "Results enquiry", "Summary data (Autumn)"],
            templates.Select(t => t.Name));
        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry, null],
            templates.Select(t => t.ExerciseType));
        Assert.Equal(["Pupils", "Results", "Summary"], templates.Select(t => t.TabName));
    }

    [Fact]
    public void The_summary_data_template_takes_one_required_summary_slot()
    {
        var summary = WindowExercises.DefaultsFor(CheckingWindowType.Post16).Single(t => t.ExerciseType is null);

        var slot = Assert.Single(summary.Datasets);
        Assert.Equal("summary", slot.Name);
        Assert.True(slot.Required);
        Assert.Null(slot.Included);
    }

    [Fact]
    public void Kind_templates_carry_the_window_types_dataset_slots()
    {
        var pupilData = WindowExercises.DefaultsFor(CheckingWindowType.Post16)
            .Single(t => t.ExerciseType == CheckingExerciseType.PupilData);

        Assert.Equal(["included", "nonincluded"], pupilData.Datasets.Select(d => d.Name));
    }

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS2)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    public void Every_other_type_starts_with_pupil_data_only(CheckingWindowType type)
    {
        var template = Assert.Single(WindowExercises.DefaultsFor(type));
        Assert.Equal(CheckingExerciseType.PupilData, template.ExerciseType);
        Assert.Equal("Pupil data checking", template.Name);
    }

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS2)]
    [InlineData(CheckingWindowType.Post16)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    public void Sort_orders_are_distinct_within_a_window_type(CheckingWindowType type)
    {
        var orders = WindowExercises.DefaultsFor(type).Select(t => t.SortOrder).ToList();

        Assert.Equal(orders.Count, orders.Distinct().Count());
    }

    [Fact]
    public void Sort_order_is_stable_and_distinct_across_every_exercise_kind()
    {
        // The order drives the wizard's date pages and every per-exercise list, so two types must
        // never share a position.
        var orders = Enum.GetValues<CheckingExerciseType>()
            .Select(WindowExercises.SortOrderFor)
            .ToList();

        Assert.Equal(orders.Count, orders.Distinct().Count());
    }

    [Fact]
    public void Display_only_sort_order_start_sorts_after_every_exercise_kind()
    {
        foreach (var kind in Enum.GetValues<CheckingExerciseType>())
        {
            Assert.True(WindowExercises.DisplayOnlySortOrderStart > WindowExercises.SortOrderFor(kind));
        }
    }

    [Fact]
    public void A_template_becomes_a_dto_with_its_name_kind_dates_and_slots()
    {
        var template = WindowExercises.DefaultsFor(CheckingWindowType.Post16).Single(t => t.ExerciseType is null);

        var dto = template.ToDto(new DateTime(2026, 10, 7), new DateTime(2026, 10, 18));

        Assert.Null(dto.ExerciseType);
        Assert.Equal("Summary data (Autumn)", dto.Name);
        Assert.Equal("Summary", dto.TabName);
        Assert.Equal(template.SortOrder, dto.SortOrder);
        Assert.Equal("summary", Assert.Single(dto.Datasets).Name);
        Assert.Equal(new DateTime(2026, 10, 7), dto.StartDate);
        Assert.Equal(new DateTime(2026, 10, 18), dto.EndDate);
    }

    [Fact]
    public void ToDto_copies_every_slot_field_not_just_the_name()
    {
        var template = WindowExercises.DefaultsFor(CheckingWindowType.Post16)
            .Single(t => t.ExerciseType == CheckingExerciseType.ResultsEnquiry);
        var expectedSlots = WindowDatasets.DefaultsFor(CheckingWindowType.Post16, CheckingExerciseType.ResultsEnquiry);

        var dto = template.ToDto(new DateTime(2026, 10, 7), new DateTime(2026, 10, 18));

        Assert.Equal(expectedSlots.Count, dto.Datasets.Count);
        for (var i = 0; i < expectedSlots.Count; i++)
        {
            Assert.Equal(expectedSlots[i].Name, dto.Datasets[i].Name);
            Assert.Equal(expectedSlots[i].SourceFile, dto.Datasets[i].SourceFile);
            Assert.Equal(expectedSlots[i].Required, dto.Datasets[i].Required);
            Assert.Equal(expectedSlots[i].SortOrder, dto.Datasets[i].SortOrder);
        }
    }

    [Fact]
    public void ToDto_gives_each_call_its_own_independent_slot_copies()
    {
        var template = WindowExercises.DefaultsFor(CheckingWindowType.Post16).Single(t => t.ExerciseType is null);

        var first = template.ToDto(new DateTime(2026, 10, 7), new DateTime(2026, 10, 18));
        var second = template.ToDto(new DateTime(2026, 10, 7), new DateTime(2026, 10, 18));
        first.Datasets[0].IngressFile = "x";

        Assert.False(ReferenceEquals(first.Datasets[0], second.Datasets[0]));
        Assert.Equal(string.Empty, template.Datasets[0].IngressFile);
        Assert.Equal(string.Empty, second.Datasets[0].IngressFile);
    }

    // ---- ChoicesFor ----

    [Fact]
    public void ChoicesFor_lists_templates_first_then_hand_added_exercises_in_their_own_sort_order()
    {
        CheckingWindowDto window = new()
        {
            Id = Guid.NewGuid(), Title = "w", KeyStage = KeyStages.Post16, CheckingWindowType = CheckingWindowType.Post16,
            StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 6, 1),
            Exercises =
            [
                new CheckingExerciseDto
                {
                    ExerciseType = CheckingExerciseType.PupilData, Name = "Pupil data checking",
                    StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 1, 14), SortOrder = 0
                },
                new CheckingExerciseDto
                {
                    ExerciseType = null, Name = "Retention",
                    StartDate = new DateTime(2027, 3, 1), EndDate = new DateTime(2027, 3, 31), SortOrder = 7
                },
                new CheckingExerciseDto
                {
                    ExerciseType = null, Name = "Late Data",
                    StartDate = new DateTime(2027, 4, 1), EndDate = new DateTime(2027, 4, 30), SortOrder = 3
                }
            ]
        };

        var choices = WindowExercises.ChoicesFor(window);

        Assert.Equal(
            ["Pupil data checking", "Results enquiry", "Summary data (Autumn)", "Late Data", "Retention"],
            choices.Select(c => c.Name));
    }

    [Fact]
    public void ChoicesFor_flags_HasFiles_only_when_a_slot_holds_both_ingress_and_schema_files()
    {
        CheckingWindowDto window = new()
        {
            Id = Guid.NewGuid(), Title = "w", KeyStage = KeyStages.KS4, CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 6, 1),
            Exercises =
            [
                new CheckingExerciseDto
                {
                    ExerciseType = CheckingExerciseType.PupilData, Name = "Pupil data checking",
                    StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 1, 14), SortOrder = 0,
                    Datasets = [new CheckingWindowDatasetDto { Name = "pupils", IngressFile = "a.csv", SchemaFile = "a.json" }]
                }
            ]
        };

        Assert.True(WindowExercises.ChoicesFor(window).Single(c => c.Name == "Pupil data checking").HasFiles);

        // Only one of the two files present — not complete, so not flagged.
        window.Exercises[0].Datasets[0].SchemaFile = "";

        Assert.False(WindowExercises.ChoicesFor(window).Single(c => c.Name == "Pupil data checking").HasFiles);
    }
}
