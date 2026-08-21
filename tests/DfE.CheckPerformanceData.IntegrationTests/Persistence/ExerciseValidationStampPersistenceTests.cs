using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// #319: the validation stamp now lives on the checking exercise, and an exercise's dates are
// editable. Both go through WindowRepository, and both were previously impossible: nothing could
// change an exercise's dates once written, and the window-level stamp was written unconditionally
// on every create and update, so it recorded nothing.
public sealed class ExerciseValidationStampPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task A_newly_created_exercise_is_not_validated()
    {
        // The old window stamp was set on every create, so every window read as validated whether
        // or not anything had been. A new exercise starts with nothing.
        Guid id = await CreateWindowAsync();

        await using var ctx = CreateContext();
        CheckingWindowDto window = (await new WindowRepository(ctx).GetByIdAsync(id, default))!;

        CheckingExerciseDto exercise = Assert.Single(window.Exercises);
        Assert.Null(exercise.ValidatedAt);
        Assert.False(exercise.IsValidated);
    }

    [Fact]
    public async Task A_stamp_written_to_an_exercise_survives_a_round_trip()
    {
        Guid id = await CreateWindowAsync();
        DateTime validatedAt = new(2027, 2, 3, 10, 30, 0, DateTimeKind.Utc);

        await using (var write = CreateContext())
        {
            var repository = new WindowRepository(write);
            CheckingWindowDto window = (await repository.GetByIdAsync(id, default))!;
            CheckingExerciseDto exercise = window.FindExercise(CheckingExerciseType.PupilData)!;

            exercise.ValidatedAt = validatedAt;
            exercise.ValidatedIngressChecksum = exercise.CurrentIngressChecksum;
            exercise.ValidatedSchemaChecksum = exercise.CurrentSchemaChecksum;

            await repository.UpdateAsync(window, default);
        }

        await using var read = CreateContext();
        CheckingWindowDto reloaded = (await new WindowRepository(read).GetByIdAsync(id, default))!;
        CheckingExerciseDto stamped = reloaded.FindExercise(CheckingExerciseType.PupilData)!;

        Assert.Equal(validatedAt, stamped.ValidatedAt!.Value.ToUniversalTime());
        Assert.True(stamped.IsValidated);
    }

    [Fact]
    public async Task Replacing_an_ingress_file_leaves_the_stored_stamp_stale_rather_than_valid()
    {
        Guid id = await CreateWindowAsync();

        await using (var write = CreateContext())
        {
            var repository = new WindowRepository(write);
            CheckingWindowDto window = (await repository.GetByIdAsync(id, default))!;
            CheckingExerciseDto exercise = window.FindExercise(CheckingExerciseType.PupilData)!;

            exercise.ValidatedAt = new DateTime(2027, 2, 3, 10, 30, 0, DateTimeKind.Utc);
            exercise.ValidatedIngressChecksum = exercise.CurrentIngressChecksum;
            exercise.ValidatedSchemaChecksum = exercise.CurrentSchemaChecksum;
            await repository.UpdateAsync(window, default);

            // The admin then swaps the CSV, exactly as the ingress step does.
            exercise.Datasets[0].IngressFile = "replacement.csv";
            exercise.Datasets[0].IngressFileChecksum = "A-DIFFERENT-CHECKSUM";
            await repository.UpdateAsync(window, default);
        }

        await using var read = CreateContext();
        CheckingWindowDto reloaded = (await new WindowRepository(read).GetByIdAsync(id, default))!;
        CheckingExerciseDto exercise2 = reloaded.FindExercise(CheckingExerciseType.PupilData)!;

        Assert.NotNull(exercise2.ValidatedAt);
        Assert.False(exercise2.IsValidated);
    }

    [Fact]
    public async Task An_existing_exercises_dates_can_be_edited()
    {
        Guid id = await CreateWindowAsync();

        await using (var write = CreateContext())
        {
            var repository = new WindowRepository(write);
            CheckingWindowDto window = (await repository.GetByIdAsync(id, default))!;
            CheckingExerciseDto exercise = window.FindExercise(CheckingExerciseType.PupilData)!;

            exercise.StartDate = new DateTime(2027, 3, 1, 9, 0, 0);
            exercise.EndDate = new DateTime(2027, 3, 20, 17, 0, 0);

            await repository.UpdateAsync(window, default);
        }

        await using var read = CreateContext();
        CheckingWindowDto reloaded = (await new WindowRepository(read).GetByIdAsync(id, default))!;
        CheckingExerciseDto edited = reloaded.FindExercise(CheckingExerciseType.PupilData)!;

        Assert.Equal(new DateTime(2027, 3, 1, 9, 0, 0), edited.StartDate);
        Assert.Equal(new DateTime(2027, 3, 20, 17, 0, 0), edited.EndDate);
    }

    [Fact]
    public async Task An_exercise_added_to_an_existing_window_is_persisted_with_its_own_dates()
    {
        Guid id = await CreateWindowAsync();

        await using (var write = CreateContext())
        {
            var repository = new WindowRepository(write);
            CheckingWindowDto window = (await repository.GetByIdAsync(id, default))!;
            window.Exercises.Add(new CheckingExerciseDto
            {
                ExerciseType = CheckingExerciseType.ResultsEnquiry,
                StartDate = new DateTime(2027, 2, 1),
                EndDate = new DateTime(2027, 6, 30, 17, 0, 0),
                SortOrder = 1
            });

            await repository.UpdateAsync(window, default);
        }

        await using var read = CreateContext();
        CheckingWindowDto reloaded = (await new WindowRepository(read).GetByIdAsync(id, default))!;
        CheckingExerciseDto added = reloaded.FindExercise(CheckingExerciseType.ResultsEnquiry)!;

        Assert.Equal(new DateTime(2027, 6, 30, 17, 0, 0), added.EndDate);
        Assert.Equal(2, reloaded.Exercises.Count);
    }

    private async Task<Guid> CreateWindowAsync()
    {
        await using var ctx = CreateContext();
        CheckingWindowDto created = await new WindowRepository(ctx).CreateAsync(new CheckingWindowDto
        {
            Title = "A window",
            StartDate = new DateTime(2027, 1, 1),
            EndDate = new DateTime(2027, 1, 14, 17, 0, 0),
            KeyStage = KeyStages.KS2,
            CheckingWindowType = CheckingWindowType.KS2,
            Exercises =
            [
                new CheckingExerciseDto
                {
                    ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = new DateTime(2027, 1, 1),
                    EndDate = new DateTime(2027, 1, 14, 17, 0, 0),
                    SortOrder = 0,
                    Datasets =
                    [
                        new CheckingWindowDatasetDto
                        {
                            Name = "pupils",
                            SortOrder = 0,
                            IngressFile = "pupils.csv",
                            IngressFileChecksum = "INGRESS-1",
                            SchemaFile = "pupils.json",
                            SchemaFileChecksum = "SCHEMA-1"
                        }
                    ]
                }
            ]
        }, default);

        return created.Id;
    }

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
            .Options, new FakeCurrentUserService());
}
