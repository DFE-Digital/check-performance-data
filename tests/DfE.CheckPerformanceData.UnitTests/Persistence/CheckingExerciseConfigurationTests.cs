using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Persistence;

public sealed class CheckingExerciseConfigurationTests
{
    // The design-time model is required (not ctx.Model) because check-constraint metadata is only
    // attached to the read-optimized model's design-time counterpart; ctx.Model alone throws when
    // a check constraint is looked up. No database is contacted either way.
    private static IModel Model()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .Options;

        using var context = new PortalDbContext(options, Substitute.For<ICurrentUserService>());
        return context.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void ExerciseType_IsOptional_SoADisplayOnlyShareNeedsNoKind()
    {
        var property = Model().FindEntityType(typeof(CheckingExercise))!.FindProperty(nameof(CheckingExercise.ExerciseType))!;

        Assert.True(property.IsNullable);
    }

    [Fact]
    public void WindowAndTypeUniqueness_AppliesOnlyToLegacyStorage()
    {
        var index = Model().FindEntityType(typeof(CheckingExercise))!.GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name)
                .SequenceEqual([nameof(CheckingExercise.CheckingWindowId), nameof(CheckingExercise.ExerciseType)]));

        Assert.True(index.IsUnique);
        Assert.Equal("\"UsesExerciseStorage\" = false", index.GetFilter());
    }

    [Fact]
    public void ATypelessExercise_MustBeADisplayOnlyShareOnExerciseStorage()
    {
        var constraint = Model().FindEntityType(typeof(CheckingExercise))!
            .GetCheckConstraints().Single(c => c.Name == "CK_CheckingExercises_TypeOrDisplayOnly");

        Assert.Equal("\"ExerciseType\" IS NOT NULL OR (\"DisplayOnly\" AND \"UsesExerciseStorage\")", constraint.Sql);
    }
}
