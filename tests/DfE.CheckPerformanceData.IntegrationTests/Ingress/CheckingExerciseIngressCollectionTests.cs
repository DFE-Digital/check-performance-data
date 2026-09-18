using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Ingress;

[Collection(nameof(PostgresCollection))]
public sealed class CheckingExerciseIngressCollectionTests(PostgresFixture postgres, AzuriteFixture azurite) : IClassFixture<AzuriteFixture>
{
    [Theory]
    [InlineData(CheckingWindowType.KS4June, 1, CheckingExerciseType.PupilData)]
    [InlineData(CheckingWindowType.Post16, 2, CheckingExerciseType.PupilData)]
    [InlineData(CheckingWindowType.Post16, 4, CheckingExerciseType.PupilData)]
    [InlineData(CheckingWindowType.Post16, 3, CheckingExerciseType.ResultsEnquiry)]
    [InlineData(CheckingWindowType.Post16, 1, null)]
    public async Task Generic_collection_persists_and_processes_every_correctly_paired_input(CheckingWindowType type, int count, CheckingExerciseType? exerciseType)
    {
        await using var db = postgres.CreateContext();
        var windows = new WindowRepository(db);
        var service = new WindowService(windows, TimeProvider.System);
        var now = DateTime.Now;
        var created = await service.CreateAsync(new CheckingWindowDto
        {
            Title = "Collection test",
            KeyStage = type == CheckingWindowType.Post16 ? KeyStages.Post16 : KeyStages.KS4,
            CheckingWindowType = type,
            StartDate = now.AddDays(-1),
            EndDate = now.AddDays(1),
            Exercises = [new CheckingExerciseDto
            {
                ExerciseType = exerciseType, DisplayOnly = exerciseType is null, StartDate = now.AddDays(-1), EndDate = now.AddDays(1),
                Name = "Collection", TabName = "Students", IsEnabled = true,
                Datasets = Enumerable.Range(0, count).Select(i => new CheckingWindowDatasetDto { Name = $"pair-{i}", SortOrder = i }).ToList()
            }]
        }, default);
        created.Title = "Updated without replacing configured definitions";
        await service.UpdateAsync(created, default);
        var persisted = (await windows.GetByIdAsync(created.Id, default))!;
        var exercise = Assert.Single(persisted.Exercises);
        Assert.Equal(count, exercise.Datasets.Count);
        var blobs = new BlobServiceClient(azurite.ConnectionString);
        var container = blobs.GetBlobContainerClient(created.Id.ToString());
        await container.CreateIfNotExistsAsync();
        var entity = await db.Set<CheckingExercise>().Include(e => e.Datasets).SingleAsync(e => e.Id == exercise.Id);
        foreach (var pair in entity.Datasets)
        {
            var value = $"record-{pair.SortOrder}";
            var csv = $"LAESTAB,VALUE\n8604070,{value}\n";
            var schema = JsonSerializer.Serialize(new
            {
                type = "object",
                required = new[] { "LAESTAB", "VALUE" },
                properties = new Dictionary<string, object>
                {
                    ["LAESTAB"] = new { type = "string" },
                    ["VALUE"] = new { type = "string", @enum = new[] { value } }
                }
            });
            pair.IngressFile = CheckingExerciseBlobPaths.DefinitionFile(entity.Id, pair.Id, "same-name.csv");
            pair.SchemaFile = CheckingExerciseBlobPaths.DefinitionFile(entity.Id, pair.Id, "same-name.json");
            Assert.Equal($"ingress/{entity.Id}/{pair.Id}/same-name.csv", pair.IngressFile);
            Assert.Equal($"ingress/{entity.Id}/{pair.Id}/same-name.json", pair.SchemaFile);
            pair.IngressFileChecksum = Hash(csv); pair.SchemaFileChecksum = Hash(schema);
            await container.GetBlobClient(pair.IngressFile).UploadAsync(BinaryData.FromString(csv));
            await container.GetBlobClient(pair.SchemaFile).UploadAsync(BinaryData.FromString(schema));
        }
        await db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var windowForPage = await new CheckYourPupilDataRepository(db,
            Substitute.For<IPupilDataBlobClient>(), cache).GetCheckingWindowAsync(created.Id);
        var uploadedDatasets = Assert.Single(windowForPage.Exercises).Datasets;
        Assert.Equal(count, uploadedDatasets.Count);
        foreach (var dataset in uploadedDatasets)
        {
            Assert.Equal(entity.Datasets.Single(d => d.Id == dataset.Id).SchemaFile, dataset.SchemaFile);
            Assert.NotNull(await new CheckingDataReader(blobs).ReadSchemaAsync(
                created.Id, dataset.SchemaFile, default));
        }
        var ingress = new CheckingExerciseIngress(new CheckingExerciseDefinitionRepository(db, windows),
            new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
                new Dictionary<string, BlobServiceClient> { ["app"] = blobs }), TimeProvider.System);
        ValidationProgress? last = null;
        await foreach (var progress in ingress.ProcessAsync(entity.Id)) last = progress;
        Assert.True(last is { IsComplete: true, IsError: false });
        Assert.Equal(count, last.RecordsProcessed);
        var visible = (await new CheckingDataCatalogue(db, TimeProvider.System).GetVisibleAsync(default)).Single(e => e.Id == entity.Id);
        var output = (await new CheckingDataReader(blobs).ReadAsync(visible, "860/4070", default))!;
        using var json = JsonDocument.Parse(output);
        Assert.Equal(Enumerable.Range(0, count).Select(i => $"record-{i}"),
            json.RootElement.EnumerateArray().Select(r => r.GetProperty("VALUE").GetString()));
        var page = new CheckingDataController(new CheckingDataCatalogue(db, TimeProvider.System),
            new CheckingDataReader(blobs), new FakeCurrentUserService(), TimeProvider.System);
        var tabs = Assert.IsType<List<CheckingDataTab>>(Assert.IsType<ViewResult>(await page.Index(default)).Model);
        Assert.Equal(count, tabs.Single(t => t.Exercise.Id == entity.Id).Rows.Count);
        Assert.Equal(1, last.FilesWritten); // Output belongs to the exercise, not to each input.
        Assert.NotNull(entity.Validated);
        if (exerciseType is null)
        {
            Assert.Null(visible.ExerciseType);
            Assert.True(visible.DisplayOnly);
            Assert.False(tabs.Single(t => t.Exercise.Id == entity.Id).CanAct);
            Assert.Equal(403, Assert.IsType<StatusCodeResult>(await page.Start(entity.Id, default)).StatusCode);
            Assert.Equal(output, Assert.IsType<FileContentResult>(await page.Download(entity.Id, default)).FileContents);
            Assert.True(await container.GetBlobClient(CheckingExerciseBlobPaths.DataBlobName(entity.Id,
                CheckingDataType.Other, "860/4070")).ExistsAsync());
        }
        Assert.Equal(count, (await new CheckingExerciseDefinitionRepository(db, windows).GetAsync(entity.Id, default))!.Exercise.Datasets.Count);

        // An incomplete required pair must fail before a clear/write, preserving the existing output.
        var first = entity.Datasets.First();
        var filename = first.IngressFile;
        first.IngressFile = "";
        await db.SaveChangesAsync();
        await foreach (var progress in ingress.ProcessAsync(entity.Id, clearExistingFiles: true)) last = progress;
        Assert.True(last!.IsError);
        Assert.Equal(output, await new CheckingDataReader(blobs).ReadAsync(visible, "8604070", default));
        first.IngressFile = filename;
        // A file must be processed using its OWN schema; a mismatched schema is rejected.
        var wrongSchema = "{\"type\":\"object\",\"required\":[\"MISSING\"],\"properties\":{\"MISSING\":{\"type\":\"string\"}}}";
        await container.GetBlobClient(first.SchemaFile).UploadAsync(BinaryData.FromString(wrongSchema), overwrite: true);
        first.SchemaFileChecksum = Hash(wrongSchema);
        await db.SaveChangesAsync();
        await foreach (var progress in ingress.ProcessAsync(entity.Id, clearExistingFiles: true)) last = progress;
        Assert.True(last!.IsError);
        Assert.Equal(output, await new CheckingDataReader(blobs).ReadAsync(visible, "8604070", default));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
