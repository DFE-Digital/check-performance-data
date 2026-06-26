using System.Text;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Web.Controllers;
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
    public async Task Import_UnsupportedSchemaVersion_SetsError_AndRedirects()
    {
        // Right schema name, but a future schema version the importer does not understand.
        var futureBundle =
            $"{{\"$schema\":\"{ContentBundle.CurrentSchema}\",\"schemaVersion\":999,\"wikiPages\":[],\"contentBlocks\":[]}}";

        var result = await _sut.Import(FileFrom(futureBundle), ContentImportMode.Skip);

        Assert.IsType<RedirectResult>(result);
        Assert.NotNull(_sut.TempData["ContentStagingError"]);
        await _staging.DidNotReceive().ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>());
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
    public async Task Import_NoFile_SetsError_AndRedirects()
    {
        var result = await _sut.Import(bundle: null, ContentImportMode.Skip);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging", redirect.Url);
        Assert.NotNull(_sut.TempData["ContentStagingError"]);
        await _staging.DidNotReceive().ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>());
    }

    [Fact]
    public async Task Import_UnsupportedSchema_SetsError_AndRedirects()
    {
        var result = await _sut.Import(FileFrom("{\"$schema\":\"other-v9\",\"wikiPages\":[]}"), ContentImportMode.Skip);

        Assert.IsType<RedirectResult>(result);
        Assert.NotNull(_sut.TempData["ContentStagingError"]);
        await _staging.DidNotReceive().ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>());
    }

    [Fact]
    public async Task Import_MalformedJson_SetsError_AndRedirects()
    {
        var result = await _sut.Import(FileFrom("not json at all"), ContentImportMode.Skip);

        Assert.IsType<RedirectResult>(result);
        Assert.NotNull(_sut.TempData["ContentStagingError"]);
        await _staging.DidNotReceive().ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>());
    }

    [Fact]
    public async Task Import_ValidBundle_CallsImportWithChosenMode_AndReportsSummary()
    {
        _staging.ImportAsync(Arg.Any<ContentBundle>(), ContentImportMode.Replace)
            .Returns(new ContentImportResult { WikiPagesCreated = 1 });

        var result = await _sut.Import(FileFrom(ValidBundleJson()), ContentImportMode.Replace);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging", redirect.Url);
        Assert.NotNull(_sut.TempData["ContentStagingResult"]);
        await _staging.Received(1).ImportAsync(
            Arg.Is<ContentBundle>(b => b.WikiPages.Count == 1), ContentImportMode.Replace);
    }

    [Fact]
    public async Task Import_Conflict_SetsError()
    {
        _staging.ImportAsync(Arg.Any<ContentBundle>(), ContentImportMode.Fail)
            .Returns<ContentImportResult>(_ => throw new ContentImportConflictException("clash"));

        var result = await _sut.Import(FileFrom(ValidBundleJson()), ContentImportMode.Fail);

        Assert.IsType<RedirectResult>(result);
        Assert.Equal("clash", _sut.TempData["ContentStagingError"]);
    }

    [Fact]
    public async Task Import_WithWarnings_ExposesThemInTempData()
    {
        var withWarning = new ContentImportResult { WikiPagesSkipped = 1 };
        withWarning.Warnings.Add("Skipped 'x/y' — parent 'x' not found.");
        _staging.ImportAsync(Arg.Any<ContentBundle>(), ContentImportMode.Skip).Returns(withWarning);

        await _sut.Import(FileFrom(ValidBundleJson()), ContentImportMode.Skip);

        Assert.NotNull(_sut.TempData["ContentStagingWarnings"]);
    }
}
