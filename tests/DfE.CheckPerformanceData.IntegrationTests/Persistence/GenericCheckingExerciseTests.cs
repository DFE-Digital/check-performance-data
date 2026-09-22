using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// #466 slice 1: a window may hold any number of display-only exercises (null kind) but still only
// one exercise per kind. The migration names every existing row from its kind. Each test gets its
// own container because one of them applies the chain only part-way.
public sealed class GenericCheckingExerciseTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    // The migration immediately before Name/TabName and the nullable kind arrive.
    private const string BeforeMigration = "20260916103225_AlignEgressWithLdsSpecV24";

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
            .Options, new FakeCurrentUserService());

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static CheckingWindow Window(params CheckingExercise[] exercises) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Post-16 2026",
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2026, 10, 7),
        EndDate = new DateTime(2027, 3, 31),
        CheckingExercises = exercises.ToList()
    };

    private static CheckingExercise Exercise(CheckingExerciseType? kind, string name, int sortOrder) => new()
    {
        ExerciseType = kind,
        Name = name,
        TabName = name,
        StartDate = new DateTime(2026, 10, 7),
        EndDate = new DateTime(2026, 10, 18),
        SortOrder = sortOrder
    };

    [Fact]
    public async Task Two_display_only_exercises_may_share_a_window()
    {
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        ctx.CheckingWindows.Add(Window(
            Exercise(null, "Summary data (Autumn)", 2),
            Exercise(null, "Retention", 3)));

        await ctx.SaveChangesAsync();

        Assert.Equal(2, await ctx.CheckingExercises.CountAsync(e => e.ExerciseType == null));
    }

    [Fact]
    public async Task Two_exercises_of_the_same_kind_are_refused()
    {
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        ctx.CheckingWindows.Add(Window(
            Exercise(CheckingExerciseType.PupilData, "Pupil data checking", 0),
            Exercise(CheckingExerciseType.PupilData, "Pupil data again", 1)));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Equal("23505", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task The_migration_names_existing_rows_from_their_kind()
    {
        await using (var ctx = CreateContext())
        {
            await ctx.Database.GetService<IMigrator>().MigrateAsync(BeforeMigration);
        }

        var windowId = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO "CheckingWindows"
                ("Id", "Title", "KeyStage", "CheckingWindowType", "StartDate", "EndDate", "Published",
                 "IngressFile", "IngressFileChecksum", "SchemaFile", "SchemaFileChecksum", "TurnaroundCommitment")
            VALUES ('{windowId}', 'w', 'Post16', 'Post16', '2026-10-07', '2027-03-31', false, '', '', '', '', '');
            INSERT INTO "CheckingExercises" ("Id", "CheckingWindowId", "ExerciseType", "StartDate", "EndDate", "SortOrder")
            VALUES ('{Guid.NewGuid()}', '{windowId}', 'PupilData', '2026-10-07', '2026-10-18', 0),
                   ('{Guid.NewGuid()}', '{windowId}', 'ResultsEnquiry', '2026-10-07', '2027-03-31', 1);
            """);

        await using (var ctx = CreateContext())
        {
            await ctx.Database.MigrateAsync();
        }

        await using (var ctx = CreateContext())
        {
            var rows = await ctx.CheckingExercises.OrderBy(e => e.SortOrder)
                .Select(e => new { e.Name, e.TabName }).ToListAsync();
            Assert.Equal(["Pupil data checking", "Results enquiry"], rows.Select(r => r.Name));
            Assert.Equal(["Pupils", "Results"], rows.Select(r => r.TabName));
        }
    }

    [Fact]
    public async Task Update_keeps_two_display_only_exercises_apart_by_id()
    {
        // A fresh context per phase: on the context that seeded the graph everything is already
        // tracked, so a missing Include in the repository's own load would go unnoticed.
        var window = Window(
            Exercise(null, "Summary data (Autumn)", 2),
            Exercise(null, "Retention", 3));
        await using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            seed.CheckingWindows.Add(window);
            await seed.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repository = new WindowRepository(ctx);
            var dto = (await repository.GetByIdAsync(window.Id, CancellationToken.None))!;
            dto.Exercises.Single(e => e.Name == "Retention").EndDate = new DateTime(2027, 3, 31);
            await repository.UpdateAsync(dto, CancellationToken.None);
        }

        await using var verify = CreateContext();
        var reloaded = (await new WindowRepository(verify).GetByIdAsync(window.Id, CancellationToken.None))!;
        Assert.Equal(2, reloaded.Exercises.Count);
        Assert.Equal(new DateTime(2027, 3, 31), reloaded.Exercises.Single(e => e.Name == "Retention").EndDate);
        Assert.Equal(new DateTime(2026, 10, 18), reloaded.Exercises.Single(e => e.Name == "Summary data (Autumn)").EndDate);
    }

    [Fact]
    public async Task Update_removes_an_exercise_the_dto_no_longer_lists_and_its_datasets()
    {
        var window = Window(
            Exercise(CheckingExerciseType.PupilData, "Pupil data checking", 0),
            Exercise(null, "Retention", 3));
        window.CheckingExercises[1].Datasets.Add(new CheckingWindowDataset
        {
            CheckingWindowId = window.Id, Name = "data", SortOrder = 0
        });
        await using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            seed.CheckingWindows.Add(window);
            await seed.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repository = new WindowRepository(ctx);
            var dto = (await repository.GetByIdAsync(window.Id, CancellationToken.None))!;
            dto.Exercises.RemoveAll(e => e.Name == "Retention");
            await repository.UpdateAsync(dto, CancellationToken.None);
        }

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.CheckingExercises.CountAsync());
        Assert.Equal(0, await verify.CheckingWindowDatasets.CountAsync(d => d.Name == "data"));
    }

    [Fact]
    public async Task HasChangeRequests_is_true_only_for_an_exercise_with_rows()
    {
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
        var window = Window(Exercise(CheckingExerciseType.PupilData, "Pupil data checking", 0));
        ctx.CheckingWindows.Add(window);
        await ctx.SaveChangesAsync();
        var exerciseId = window.CheckingExercises[0].Id;
        ctx.ChangeRequests.Add(new ChangeRequest
        {
            Id = Guid.NewGuid(),
            WindowId = window.Id,
            CheckingExerciseId = exerciseId,
            OrganisationUrn = 1,
            PupilUpn = "A1",
            Submitted = new DateTime(2026, 10, 8, 9, 0, 0),
            SubmittedById = Guid.NewGuid(),
            SubmittedByName = "Test User",
            Status = RequestStatus.InProgress,
            ReferenceNumber = "REF-1",
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove",
            AmendmentType = WhatToChange.Remove
        });
        await ctx.SaveChangesAsync();

        var repository = new WindowRepository(ctx);

        Assert.True(await repository.HasChangeRequestsAsync(exerciseId, CancellationToken.None));
        Assert.False(await repository.HasChangeRequestsAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
