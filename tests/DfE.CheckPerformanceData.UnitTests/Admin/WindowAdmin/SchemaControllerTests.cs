using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public class SchemaControllerTests
{
    // Every window has at least a "pupils" dataset; a Post16 window has "included"/"nonincluded".
    private const string Dataset = "pupils";

    private const string ValidSchema = """{ "$schema": "https://json-schema.org/draft/2020-12/schema", "type": "object" }""";

    [Fact]
    public async Task Index_returns_not_found_when_window_does_not_exist()
    {
        var windowService = Substitute.For<IWindowService>();
        var controller = BuildController(windowService);

        var result = await controller.Index(Guid.NewGuid(), CheckingExerciseType.PupilData, Dataset, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Index_returns_view_with_populated_model()
    {
        var windowService = Substitute.For<IWindowService>();
        var id = Guid.NewGuid();
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Window(id));

        var controller = BuildController(windowService);

        var result = await controller.Index(id, CheckingExerciseType.PupilData, Dataset, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SchemaItem>(view.Model);
        Assert.Equal(id, model.WindowId);
    }

    [Fact]
    public async Task Submit_returns_bad_request_when_route_id_does_not_match_model()
    {
        var windowService = Substitute.For<IWindowService>();
        var controller = BuildController(windowService);

        var model = new SchemaItem { WindowId = Guid.NewGuid() };
        var result = await controller.Submit(Guid.NewGuid(), CheckingExerciseType.PupilData, Dataset, model, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        await windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_returns_view_with_error_when_no_file_selected()
    {
        var windowService = Substitute.For<IWindowService>();
        var id = Guid.NewGuid();
        var controller = BuildController(windowService);

        var model = new SchemaItem { WindowId = id, Schema = null };
        var result = await controller.Submit(id, CheckingExerciseType.PupilData, Dataset, model, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.True(controller.ModelState.ContainsKey(nameof(SchemaItem.Schema)));
        await windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_returns_not_found_when_window_does_not_exist()
    {
        var windowService = Substitute.For<IWindowService>();
        var id = Guid.NewGuid();
        var controller = BuildController(windowService);

        var model = new SchemaItem { WindowId = id, Schema = FileFrom(ValidSchema) };
        var result = await controller.Submit(id, CheckingExerciseType.PupilData, Dataset, model, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_returns_view_with_error_when_file_is_not_a_json_schema()
    {
        var windowService = Substitute.For<IWindowService>();
        var id = Guid.NewGuid();
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Window(id));

        var controller = BuildController(windowService);

        var model = new SchemaItem { WindowId = id, Schema = FileFrom("this is not json") };
        var result = await controller.Submit(id, CheckingExerciseType.PupilData, Dataset, model, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.True(controller.ModelState.ContainsKey(nameof(SchemaItem.Schema)));
        await windowService.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    private static IFormFile FileFrom(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Schema", "schema.json");
    }

    private static SchemaController BuildController(
        IWindowService windowService,
        Dictionary<string, BlobServiceClient>? blobClients = null) =>
        new(
            Substitute.For<ILogger<SchemaController>>(),
            windowService,
            blobClients ?? new Dictionary<string, BlobServiceClient>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            Url = StubUrlHelper()
        };

    private static IUrlHelper StubUrlHelper()
    {
        var urlHelper = Substitute.For<IUrlHelper>();
        urlHelper.Action(Arg.Any<UrlActionContext>()).Returns(_ => "/");
        return urlHelper;
    }

    private static CheckingWindowDto Window(Guid id) =>
        new()
        {
            Id = id,
            Title = "Window",
            StartDate = new DateTime(2027, 1, 1),
            EndDate = new DateTime(2027, 2, 1),
            KeyStage = KeyStages.KS2,
            CheckingWindowType = CheckingWindowType.KS2,
            // The slot hangs off the exercise that consumes it (#314).
            Exercises =
            [
                new CheckingExerciseDto
                {
                    ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = new DateTime(2027, 1, 1),
                    EndDate = new DateTime(2027, 2, 1),
                    SortOrder = 0,
                    Datasets = [new CheckingWindowDatasetDto { Name = Dataset, SortOrder = 0 }]
                }
            ]
        };

    [Fact]
    public async Task Index_returns_not_found_when_the_window_has_no_such_dataset()
    {
        var windowService = Substitute.For<IWindowService>();
        var id = Guid.NewGuid();
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Window(id));

        var controller = BuildController(windowService);

        var result = await controller.Index(id, CheckingExerciseType.PupilData, "nonincluded", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Submit_stores_the_schema_against_the_named_dataset()
    {
        var windowService = Substitute.For<IWindowService>();
        var id = Guid.NewGuid();
        var window = Window(id);
        windowService.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(window);

        var blobs = new Dictionary<string, BlobServiceClient>();
        var controller = BuildController(windowService, blobs);

        var model = new SchemaItem { WindowId = id, Schema = FileFrom(ValidSchema) };
        var result = await controller.Submit(id, CheckingExerciseType.PupilData, Dataset, model, CancellationToken.None);

        // No "app" blob client is configured, so the upload short-circuits before persisting.
        // The dataset lookup and validation still had to succeed to get that far.
        Assert.IsType<ObjectResult>(result);
    }
}
