using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.WindowAdmin;

[Collection(nameof(AzuriteCollection))]
public sealed class CheckingExerciseDataStorageTests(AzuriteFixture storage)
{
    private const string Schema = """{ "type": "object", "properties": { "VALUE": { "type": "string" } } }""";

    [Fact]
    public async Task Incoming_csv_and_reused_schema_are_copied_to_the_selected_pair_only()
    {
        var client = new BlobServiceClient(storage.ConnectionString);
        var targetWindow = Window();
        var sourceWindow = Window();
        var exercise = targetWindow.Exercises[0];
        var pair = exercise.Datasets[0];
        var other = targetWindow.Exercises[1].Datasets[0];
        var source = sourceWindow.Exercises[0].Datasets[0];
        source.SchemaFile = "reusable.json";
        var sourceContainer = client.GetBlobContainerClient(sourceWindow.Id.ToString());
        await sourceContainer.CreateIfNotExistsAsync();
        await sourceContainer.GetBlobClient("schema/reusable.json").UploadAsync(BinaryData.FromString(Schema));

        var incoming = client.GetBlobContainerClient("incoming-" + Guid.NewGuid().ToString("N"));
        await incoming.CreateIfNotExistsAsync();
        const string csv = "VALUE\nexample\n";
        await incoming.GetBlobClient("folder/input.csv").UploadAsync(BinaryData.FromString(csv));
        await incoming.GetBlobClient("folder/not-a-csv.json").UploadAsync(BinaryData.FromString("{}"));
        var service = Service(targetWindow, sourceWindow);
        var clients = new Dictionary<string, BlobServiceClient> { ["app"] = client, ["ingress"] = client };
        var csvController = new IngressFileController(NullLogger<IngressFileController>.Instance, service, clients);
        Configure(csvController);
        var browse = Assert.IsType<IngressFolderBrowseViewModel>(Assert.IsType<ViewResult>(
            await csvController.Browse(targetWindow.Id, exercise.ExerciseType, pair.Name, incoming.Name, "folder/", default, exercise.Id, true)).Model);
        Assert.Equal(new[] { "folder/input.csv" }, browse.Files);
        Assert.True(browse.ReturnToExercise);
        Assert.Contains(exercise.Id.ToString(), browse.CancelUrl);

        var csvRedirect = Assert.IsType<RedirectToActionResult>(await csvController.Select(
            targetWindow.Id, exercise.ExerciseType, pair.Name, incoming.Name + "/folder/input.csv", default, exercise.Id, true));
        Assert.Equal("EditCheckingExercise", csvRedirect.ControllerName);
        Assert.Equal(exercise.Id, csvRedirect.RouteValues!["exerciseId"]);
        Assert.Equal(CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, pair.Id, "input.csv"), pair.IngressFile);
        Assert.Equal(Hash(csv), pair.IngressFileChecksum);
        Assert.Empty(other.IngressFile);

        var schemaController = new SchemaController(NullLogger<SchemaController>.Instance, service, clients);
        Configure(schemaController);
        var schemaPage = Assert.IsType<SchemaItem>(Assert.IsType<ViewResult>(await schemaController.Index(
            targetWindow.Id, exercise.ExerciseType, pair.Name, default, exercise.Id, true)).Model);
        Assert.Contains(schemaPage.ExistingSchemas, s => s.DatasetId == source.Id);
        var schemaRedirect = Assert.IsType<RedirectToActionResult>(await schemaController.Submit(
            targetWindow.Id, exercise.ExerciseType, pair.Name,
            new() { WindowId = targetWindow.Id, ExistingSchemaDatasetId = source.Id }, default, exercise.Id, true));
        Assert.Equal("EditCheckingExercise", schemaRedirect.ControllerName);
        Assert.Equal(exercise.Id, schemaRedirect.RouteValues!["exerciseId"]);
        Assert.Equal(CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, pair.Id, "reusable.json"), pair.SchemaFile);
        Assert.Equal(Hash(Schema), pair.SchemaFileChecksum);
        Assert.Empty(other.SchemaFile);
        var targetContainer = client.GetBlobContainerClient(targetWindow.Id.ToString());
        Assert.Equal(csv, (await targetContainer.GetBlobClient(pair.IngressFile).DownloadContentAsync()).Value.Content.ToString());
        Assert.Equal(Schema, (await targetContainer.GetBlobClient(pair.SchemaFile).DownloadContentAsync()).Value.Content.ToString());
        Assert.Equal(Schema, (await sourceContainer.GetBlobClient("schema/reusable.json").DownloadContentAsync()).Value.Content.ToString());
        await service.Received(2).UpdateAsync(targetWindow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalid_or_missing_reused_schema_does_not_replace_the_existing_pair()
    {
        var client = new BlobServiceClient(storage.ConnectionString);
        var window = Window();
        var sourceWindow = Window();
        var source = sourceWindow.Exercises[0].Datasets[0];
        source.SchemaFile = "invalid.json";
        var container = client.GetBlobContainerClient(sourceWindow.Id.ToString());
        await container.CreateIfNotExistsAsync();
        await container.GetBlobClient("schema/invalid.json").UploadAsync(BinaryData.FromString("not json"));
        var service = Service(window, sourceWindow);
        var controller = new SchemaController(NullLogger<SchemaController>.Instance, service,
            new Dictionary<string, BlobServiceClient> { ["app"] = client });
        Configure(controller);
        var exercise = window.Exercises[0];
        var pair = exercise.Datasets[0];
        pair.SchemaFile = "existing.json";
        pair.SchemaFileChecksum = "existing-checksum";
        var model = new SchemaItem { WindowId = window.Id, ExistingSchemaDatasetId = source.Id };
        Assert.IsType<ViewResult>(await controller.Submit(window.Id, exercise.ExerciseType, pair.Name, model, default, exercise.Id, true));
        Assert.Contains(nameof(model.ExistingSchemaDatasetId), controller.ModelState.Keys);
        Assert.Equal("existing.json", pair.SchemaFile);
        Assert.Equal("existing-checksum", pair.SchemaFileChecksum);
        await service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);

        controller.ModelState.Clear();
        await container.GetBlobClient("schema/invalid.json").DeleteAsync();
        Assert.IsType<ViewResult>(await controller.Submit(window.Id, exercise.ExerciseType, pair.Name, model, default, exercise.Id, true));
        Assert.Contains(nameof(model.ExistingSchemaDatasetId), controller.ModelState.Keys);
        Assert.NotEmpty(model.PostUrl!);
        Assert.NotEmpty(model.CancelUrl!);
        await service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Wrong_exercise_ids_are_rejected_before_browsing_or_copying()
    {
        var window = Window();
        var service = Service(window);
        var csv = new IngressFileController(NullLogger<IngressFileController>.Instance, service,
            new Dictionary<string, BlobServiceClient>());
        var schema = new SchemaController(NullLogger<SchemaController>.Instance, service,
            new Dictionary<string, BlobServiceClient>());
        Configure(csv);
        Configure(schema);
        Assert.IsType<NotFoundResult>(await csv.Index(window.Id, CheckingExerciseType.PupilData, "pupils", default, Guid.NewGuid(), true));
        Assert.IsType<NotFoundResult>(await schema.Index(window.Id, CheckingExerciseType.PupilData, "pupils", default, Guid.NewGuid(), true));
        Assert.IsType<NotFoundResult>(await schema.Submit(window.Id, CheckingExerciseType.PupilData, "pupils",
            new() { WindowId = window.Id, ExistingSchemaDatasetId = Guid.NewGuid() }, default, Guid.NewGuid(), true));
        await service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    private static void Configure(Controller controller)
    {
        var urls = Substitute.For<IUrlHelper>();
        urls.Action(Arg.Any<UrlActionContext>()).Returns("/exercise");
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.Url = urls;
    }

    private static IWindowService Service(params CheckingWindowDto[] windows)
    {
        var service = Substitute.For<IWindowService>();
        foreach (var window in windows)
            service.GetByIdAsync(window.Id, Arg.Any<CancellationToken>()).Returns(window);
        service.GetAllDataAsync(Arg.Any<CancellationToken>()).Returns(new PageResult { Windows = windows.ToList() });
        return service;
    }

    private static CheckingWindowDto Window() => new()
    {
        Id = Guid.NewGuid(), Title = "Data file test", CheckingWindowType = CheckingWindowType.Post16, KeyStage = KeyStages.Post16,
        StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 10, 1),
        Exercises = Enumerable.Range(0, 2).Select(i => new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), Name = $"Release {i}", ExerciseType = CheckingExerciseType.PupilData,
            StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 10, 1),
            Datasets = [new() { Id = Guid.NewGuid(), Name = "pupils", Required = true }]
        }).ToList()
    };

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
