using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Persistence;

// A dataset is an input to one exercise, not to the window (#314). These assertions pin the
// reparented relationship: the file pair hangs off the exercise that consumes it, names are unique
// within an exercise rather than within a window, and the window reaches its files only through
// its exercises. Reading ctx.Model builds the model in memory; no database is contacted.
public class CheckingWindowDatasetReparentingTests
{
    private static IModel Model()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .Options;

        using var ctx = new PortalDbContext(options, Substitute.For<ICurrentUserService>());
        return ctx.Model;
    }

    private static IEntityType DatasetEntity() => Model().FindEntityType(typeof(CheckingWindowDataset))!;

    [Fact]
    public void A_dataset_hangs_off_the_exercise_that_consumes_it()
    {
        var foreignKey = Assert.Single(DatasetEntity().GetForeignKeys());

        Assert.Equal(typeof(CheckingExercise), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(
            nameof(CheckingWindowDataset.CheckingExerciseId),
            Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(nameof(CheckingExercise.Datasets), foreignKey.PrincipalToDependent!.Name);
    }

    [Fact]
    public void Deleting_an_exercise_cascades_to_its_datasets()
    {
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(DatasetEntity().GetForeignKeys()).DeleteBehavior);
    }

    [Fact]
    public void A_dataset_name_is_unique_within_its_exercise_not_within_the_window()
    {
        var index = Assert.Single(DatasetEntity().GetIndexes());

        Assert.Equal(
            [nameof(CheckingWindowDataset.CheckingExerciseId), nameof(CheckingWindowDataset.Name)],
            index.Properties.Select(p => p.Name));
        Assert.True(index.IsUnique);
    }

    // Kept for one release so the release can be rolled back, but no longer a relationship: it is
    // a plain column with nothing pointing at it. The follow-up ticket drops it.
    [Fact]
    public void The_legacy_window_id_column_survives_as_a_plain_column()
    {
        var property = DatasetEntity().FindProperty(nameof(CheckingWindowDataset.CheckingWindowId));

        Assert.NotNull(property);
        Assert.DoesNotContain(
            DatasetEntity().GetForeignKeys(),
            fk => fk.Properties.Any(p => p.Name == nameof(CheckingWindowDataset.CheckingWindowId)));
    }

    [Fact]
    public void A_window_reaches_its_files_only_through_its_exercises()
    {
        var window = Model().FindEntityType(typeof(CheckingWindow))!;

        Assert.Null(window.FindNavigation("Datasets"));
        Assert.NotNull(window.FindNavigation(nameof(CheckingWindow.CheckingExercises)));
    }
}
