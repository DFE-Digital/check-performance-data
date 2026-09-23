using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #466 review fix: FieldFor had no test at all before this. Every string WindowService.Validate (or
// AddExerciseAsync/UpdateExerciseAsync/RemoveExerciseAsync directly) can hand back is covered here,
// so a reason's wording drifting in WindowService.cs cannot silently re-route (or stop routing) an
// error without a test noticing. Grepped from every ExerciseChangeResult.Refused(...) call in
// WindowService.cs.
public class ExerciseFormErrorsTests
{
    [Theory]
    [InlineData("Window not found", nameof(ExerciseFormItem.Name))]
    [InlineData("Exercise not found", nameof(ExerciseFormItem.Name))]
    [InlineData("A window must keep at least one exercise", nameof(ExerciseFormItem.Name))]
    [InlineData("This exercise has change requests and cannot be removed", nameof(ExerciseFormItem.Name))]
    [InlineData("Select at least one checking exercise", nameof(ExerciseFormItem.Name))]
    [InlineData("Retention has change requests and cannot be removed", nameof(ExerciseFormItem.Name))]
    [InlineData("Enter a name", nameof(ExerciseFormItem.Name))]
    [InlineData("Enter a tab name", nameof(ExerciseFormItem.TabName))]
    [InlineData("End date can not occur before the start date", nameof(ExerciseFormItem.EndDate))]
    [InlineData("An exercise with that name already exists in this window", nameof(ExerciseFormItem.Name))]
    [InlineData(null, nameof(ExerciseFormItem.Name))]
    public void FieldFor_maps_every_reason_WindowService_can_return_to_a_real_field(string? reason, string expectedField)
    {
        Assert.Equal(expectedField, ExerciseFormErrors.FieldFor(reason));
    }

    [Fact]
    public void FieldFor_routes_the_name_length_reason_to_Name()
    {
        Assert.Equal(nameof(ExerciseFormItem.Name),
            ExerciseFormErrors.FieldFor($"Name must be {ExerciseDefinition.MaxNameLength} characters or less"));
    }

    [Fact]
    public void FieldFor_routes_the_tab_name_length_reason_to_TabName()
    {
        Assert.Equal(nameof(ExerciseFormItem.TabName),
            ExerciseFormErrors.FieldFor($"Tab name must be {ExerciseDefinition.MaxTabNameLength} characters or less"));
    }

    [Theory]
    [InlineData("Window not found")]
    [InlineData("Exercise not found")]
    [InlineData("A window must keep at least one exercise")]
    [InlineData("This exercise has change requests and cannot be removed")]
    [InlineData("Select at least one checking exercise")]
    [InlineData("Retention has change requests and cannot be removed")]
    [InlineData("Enter a name")]
    [InlineData("Enter a tab name")]
    [InlineData("End date can not occur before the start date")]
    [InlineData("An exercise with that name already exists in this window")]
    [InlineData(null)]
    public void FieldFor_always_names_a_real_ExerciseFormItem_property(string? reason)
    {
        var field = ExerciseFormErrors.FieldFor(reason);

        Assert.NotNull(typeof(ExerciseFormItem).GetProperty(field));
    }

    [Fact]
    public void IsMissing_is_true_only_for_the_window_or_exercise_having_vanished()
    {
        Assert.True(ExerciseFormErrors.IsMissing("Window not found"));
        Assert.True(ExerciseFormErrors.IsMissing("Exercise not found"));
        Assert.False(ExerciseFormErrors.IsMissing("Enter a name"));
        Assert.False(ExerciseFormErrors.IsMissing(null));
    }
}
