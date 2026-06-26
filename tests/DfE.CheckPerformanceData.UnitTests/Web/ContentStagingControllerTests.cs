using System.Text;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class ContentStagingControllerTests
{
    private readonly IContentStagingService _staging = Substitute.For<IContentStagingService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly ContentStagingController _sut;

    public ContentStagingControllerTests()
    {
        _currentUser.Email.Returns("editor@education.gov.uk");
        _sut = new ContentStagingController(_staging, _currentUser)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), Substitute.For<ITempDataProvider>())
        };
    }

    private static IFormFile FileFrom(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "bundle", "bundle.json");
    }

    private static string ValidBundleJson() => ContentStagingJson.Serialize(new ContentBundle
    {
        WikiPages = [new() { SlugPath = "alpha", ParentSlugPath = "", Slug = "alpha", Title = "Alpha", Content = "a" }]
    });

    [Fact]
    public void Controller_IsGatedByEditorRole()
    {
        var attr = typeof(ContentStagingController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>()
            .Single();
        Assert.Equal(WikiConstants.EditorRole, attr.Roles);
    }

    [Fact]
    public async Task Export_ReturnsJsonFile_StampedWithSchemaAndUser()
    {
        _staging.ExportAsync().Returns(new ContentBundle
        {
            WikiPages = [new() { SlugPath = "alpha", Slug = "alpha", Title = "Alpha", Content = "a" }]
        });

        var result = await _sut.Export();

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/json", file.ContentType);
        Assert.EndsWith(".json", file.FileDownloadName);
        var json = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains(ContentBundle.CurrentSchema, json);
        Assert.Contains("editor@education.gov.uk", json);
    }

    [Fact]
    public async Task Export_StampsSchemaVersion()
    {
        _staging.ExportAsync().Returns(new ContentBundle
        {
            WikiPages = [new() { SlugPath = "alpha", Slug = "alpha", Title = "Alpha", Content = "a" }]
        });

        var result = await _sut.Export();

        var file = Assert.IsType<FileContentResult>(result);
        var json = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("schemaVersion", json);
        var roundTripped = ContentStagingJson.Deserialize(json)!;
        Assert.Equal(ContentBundle.CurrentSchemaVersion, roundTripped.SchemaVersion);
    }

    [Fact]
    public async Task Preview_UnsupportedSchemaVersion_SetsError_AndRedirects()
    {
        // Right schema name, but a future schema version the importer does not understand.
        var futureBundle =
            $"{{\"$schema\":\"{ContentBundle.CurrentSchema}\",\"schemaVersion\":999,\"wikiPages\":[],\"contentBlocks\":[]}}";

        var result = await _sut.Preview(FileFrom(futureBundle));

        Assert.IsType<RedirectResult>(result);
        Assert.NotNull(_sut.TempData["ContentStagingError"]);
        await _staging.DidNotReceive().PreviewAsync(Arg.Any<ContentBundle>());
    }

    [Fact]
    public async Task Select_ReturnsCatalogView()
    {
        var catalog = new ContentCatalog(
            [new(Guid.NewGuid(), "Alpha", "alpha", 0, default, default)],
            [new(Guid.NewGuid(), "footer", "Content", "/home", default, default)]);
        _staging.GetCatalogAsync().Returns(catalog);

        var result = await _sut.Select();

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(catalog, view.Model);
    }

    [Fact]
    public async Task ExportSelected_WithSelection_ReturnsJsonFile_PassingChosenIds()
    {
        var pageId = Guid.NewGuid();
        var blockId = Guid.NewGuid();
        _staging.ExportAsync(Arg.Any<ContentExportSelection>()).Returns(new ContentBundle
        {
            WikiPages = [new() { Id = pageId, Slug = "alpha", Title = "Alpha" }]
        });

        var result = await _sut.ExportSelected([pageId], [blockId]);

        Assert.IsType<FileContentResult>(result);
        await _staging.Received(1).ExportAsync(Arg.Is<ContentExportSelection>(
            s => s.WikiPageIds.Contains(pageId) && s.ContentBlockIds.Contains(blockId)));
    }

    [Fact]
    public async Task ExportSelected_NothingChosen_SetsError_AndRedirects()
    {
        var result = await _sut.ExportSelected(null, null);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging/select", redirect.Url);
        Assert.NotNull(_sut.TempData["ContentStagingError"]);
        await _staging.DidNotReceive().ExportAsync(Arg.Any<ContentExportSelection>());
    }

    [Fact]
    public async Task Preview_NoFile_SetsError_AndRedirects()
    {
        var result = await _sut.Preview(bundle: null);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging", redirect.Url);
        Assert.NotNull(_sut.TempData["ContentStagingError"]);
        await _staging.DidNotReceive().PreviewAsync(Arg.Any<ContentBundle>());
    }

    [Fact]
    public async Task Preview_UnsupportedSchema_SetsError_AndRedirects()
    {
        var result = await _sut.Preview(FileFrom("{\"$schema\":\"other-v9\",\"wikiPages\":[]}"));

        Assert.IsType<RedirectResult>(result);
        Assert.NotNull(_sut.TempData["ContentStagingError"]);
        await _staging.DidNotReceive().PreviewAsync(Arg.Any<ContentBundle>());
    }

    [Fact]
    public async Task Preview_MalformedJson_SetsError_AndRedirects()
    {
        var result = await _sut.Preview(FileFrom("not json at all"));

        Assert.IsType<RedirectResult>(result);
        Assert.NotNull(_sut.TempData["ContentStagingError"]);
        await _staging.DidNotReceive().PreviewAsync(Arg.Any<ContentBundle>());
    }

    [Fact]
    public async Task Preview_ValidFile_ReturnsPreviewView_WithBundleJson()
    {
        _staging.PreviewAsync(Arg.Any<ContentBundle>()).Returns(new ContentImportPreview([], []));

        var result = await _sut.Preview(FileFrom(ValidBundleJson()));

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ImportPreviewViewModel>(view.Model);
        Assert.Contains(ContentBundle.CurrentSchema, model.BundleJson);
        await _staging.Received(1).PreviewAsync(Arg.Is<ContentBundle>(b => b.WikiPages.Count == 1));
    }

    [Fact]
    public async Task Import_NoBundleJson_SetsError_AndRedirects()
    {
        var result = await _sut.Import(new ImportConfirmFormModel { BundleJson = null });

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging", redirect.Url);
        Assert.NotNull(_sut.TempData["ContentStagingError"]);
    }

    [Fact]
    public async Task Import_Confirm_CallsImportWithGlobalModeAndPerItemDecisions()
    {
        var id = Guid.NewGuid();
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns(new ContentImportResult { WikiPagesUpdated = 1 });

        var model = new ImportConfirmFormModel
        {
            BundleJson = ValidBundleJson(),
            GlobalMode = ContentImportMode.Skip,
            Decisions = [new() { Id = id, Action = ContentImportMode.Replace }]
        };

        var result = await _sut.Import(model);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging", redirect.Url);
        Assert.NotNull(_sut.TempData["ContentStagingResult"]);
        await _staging.Received(1).ImportAsync(
            Arg.Any<ContentBundle>(), ContentImportMode.Skip,
            Arg.Is<IReadOnlyDictionary<Guid, ContentImportMode>>(d => d[id] == ContentImportMode.Replace));
    }

    [Fact]
    public async Task Import_Confirm_OmitsUseDefaultDecisions()
    {
        // A null Action (the "use default" radio) must not appear in the decisions dictionary.
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns(new ContentImportResult());

        var model = new ImportConfirmFormModel
        {
            BundleJson = ValidBundleJson(),
            GlobalMode = ContentImportMode.Replace,
            Decisions = [new() { Id = Guid.NewGuid(), Action = null }]
        };

        await _sut.Import(model);

        await _staging.Received(1).ImportAsync(
            Arg.Any<ContentBundle>(), ContentImportMode.Replace,
            Arg.Is<IReadOnlyDictionary<Guid, ContentImportMode>>(d => d.Count == 0));
    }

    [Fact]
    public async Task Import_Confirm_Conflict_SetsError()
    {
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns<ContentImportResult>(_ => throw new ContentImportConflictException("clash"));

        var result = await _sut.Import(new ImportConfirmFormModel
        {
            BundleJson = ValidBundleJson(),
            GlobalMode = ContentImportMode.Fail
        });

        Assert.IsType<RedirectResult>(result);
        Assert.Equal("clash", _sut.TempData["ContentStagingError"]);
    }

    [Fact]
    public async Task Import_Confirm_WithWarnings_ExposesThemInTempData()
    {
        var withWarning = new ContentImportResult { WikiPagesSkipped = 1 };
        withWarning.Warnings.Add("Skipped 'x/y' — parent 'x' not found.");
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns(withWarning);

        await _sut.Import(new ImportConfirmFormModel { BundleJson = ValidBundleJson() });

        Assert.NotNull(_sut.TempData["ContentStagingWarnings"]);
    }
}
