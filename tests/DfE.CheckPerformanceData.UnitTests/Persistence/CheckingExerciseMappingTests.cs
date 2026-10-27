using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Persistence;

// A checking window holds many checking exercises, each on its own dates (#307). These assertions
// pin the parts of the mapping the rest of the epic depends on: the enum is stored as a string so
// a new exercise type needs no schema change, the dates use the same unspecified-kind column type
// as the window's own dates, and one exercise type may appear only once per window — which is the
// lookup ICheckingExerciseService (#315) relies on. Reading ctx.Model builds the model in memory;
// no database is contacted.
public class CheckingExerciseMappingTests
{
    private static IEntityType ExerciseEntity()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .Options;

        using var ctx = new PortalDbContext(options, Substitute.For<ICurrentUserService>());
        return ctx.Model.FindEntityType(typeof(CheckingExercise))!;
    }

    [Fact]
    public void CheckingExercise_is_mapped_to_its_own_table()
    {
        Assert.Equal("CheckingExercises", ExerciseEntity().GetTableName());
    }

    [Fact]
    public void ExerciseType_is_stored_as_a_string_so_a_new_type_needs_no_schema_change()
    {
        var property = ExerciseEntity().FindProperty(nameof(CheckingExercise.ExerciseType))!;

        Assert.Equal(typeof(string), property.GetTypeMapping().Converter!.ProviderClrType);
    }

    [Theory]
    [InlineData(nameof(CheckingExercise.StartDate))]
    [InlineData(nameof(CheckingExercise.EndDate))]
    public void Dates_use_the_same_column_type_as_the_window_dates(string propertyName)
    {
        var property = ExerciseEntity().FindProperty(propertyName)!;

        Assert.Equal("timestamp without time zone", property.GetColumnType());
        Assert.False(property.IsNullable);
    }

    [Fact]
    public void One_exercise_type_may_appear_only_once_per_window()
    {
        var index = Assert.Single(
            ExerciseEntity().GetIndexes(),
            i => i.Properties.Select(p => p.Name).SequenceEqual(
                new[] { nameof(CheckingExercise.CheckingWindowId), nameof(CheckingExercise.ExerciseType) }));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Deleting_a_window_cascades_to_its_exercises()
    {
        var foreignKey = Assert.Single(ExerciseEntity().GetForeignKeys());

        Assert.Equal(typeof(CheckingWindow), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal(
            nameof(CheckingWindow.CheckingExercises),
            foreignKey.PrincipalToDependent!.Name);
    }

    // Every type stores as its own member name, and the column is wide enough for all of them, so
    // adding a member needs no schema change (#307). The names are the stored form the migration
    // backfill writes as a literal ('PupilData'), so changing one is a data change, not a rename.
    [Fact]
    public void Every_exercise_type_stores_as_its_own_name()
    {
        var property = ExerciseEntity().FindProperty(nameof(CheckingExercise.ExerciseType))!;
        var converter = property.GetTypeMapping().Converter!;

        var stored = Enum.GetValues<CheckingExerciseType>()
            .Select(type => (string)converter.ConvertToProvider(type)!)
            .ToList();

        Assert.Equal(["PupilData", "ResultsEnquiry"], stored);
        Assert.All(stored, name => Assert.True(
            name.Length <= property.GetMaxLength(),
            $"'{name}' does not fit the ExerciseType column."));
    }
}
