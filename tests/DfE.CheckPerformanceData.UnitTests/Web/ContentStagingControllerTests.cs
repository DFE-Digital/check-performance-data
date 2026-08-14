using System.Reflection;
using System.Text;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class ContentStagingControllerTests
{
    private readonly IContentStagingService _staging = Substitute.For<IContentStagingService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IPageNodeRepository _pageNodeRepository = Substitute.For<IPageNodeRepository>();
    private readonly ILogger<ContentStagingController> _logger = Substitute.For<ILogger<ContentStagingController>>();
    private readonly IContentStagingSessionStore _sessions = Substitute.For<IContentStagingSessionStore>();
    private readonly ContentStagingController _sut;

    // Registers a stored bundle with the session-store substitute and hands back its id, so a
    // test can post the confirm form the way the preview page would.
    private Guid SessionWith(string bundleJson)
    {
        var id = Guid.NewGuid();
        _sessions.GetBundleJsonAsync(id, Arg.Any<CancellationToken>()).Returns(bundleJson);
        return id;
    }

    public ContentStagingControllerTests()
    {
        _currentUser.Email.Returns("editor@education.gov.uk");
        _sut = Build(devToolsEnabled: true, environment: "Development");
    }


    // Clear-all is gated the way the other destructive dev surfaces are: the Dev:ToolsEnabled
    // flag AND not-production. Build a controller for a given environment so each case is explicit.
    private ContentStagingController Build(
        bool devToolsEnabled,
        string environment,
        bool showButton = true,
        IPortalDbContext? dbContext = null,
        IContentStagingLock? importLock = null)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dev:ToolsEnabled"] = devToolsEnabled ? "true" : "false"
            })
            .Build();

        var host = Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>();
        host.EnvironmentName.Returns(environment);

        var settings = Substitute.For<DfE.CheckPerformanceData.Application.Settings.ISettingService>();
        settings.GetBoolAsync(DfE.CheckPerformanceData.Application.Settings.SettingKeys.ShowDeleteAllButton)
            .Returns(showButton);

        return new ContentStagingController(
            _staging, _currentUser, _pageNodeRepository, _logger, configuration, host, settings, _sessions, dbContext, importLock)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), Substitute.For<ITempDataProvider>())
        };
    }

    // --- Clear-all environment gate (issue: hide/remove Clear All from non-dev environments) ---

    // The destructive endpoint must not merely be hidden in the UI. A hidden button still leaves a
    // POST route reachable by anyone holding the content-staging grant who knows the URL, and the
    // action truncates every page and content block in the environment.
    [Theory]
    [InlineData(false, "QA")]            // deployed QA: neither Development nor the flag
    [InlineData(false, "Preproduction")]
    [InlineData(true,  "Production")]    // even if a prod deploy leaves the flag on
    [InlineData(false, "Production")]
    public async Task ClearAll_WhenNotADevSurface_Returns404_AndTouchesNothing(bool devTools, string environment)
    {
        var sut = Build(devTools, environment);

        var result = await sut.ClearAll();

        Assert.IsType<NotFoundResult>(result);
        await _pageNodeRepository.DidNotReceiveWithAnyArgs().TruncateAllContentAsync();
    }

    [Theory]
    [InlineData(true,  "Development")]   // local dev
    [InlineData(false, "Development")]   // deployed DEV: environment name only, no flag
    [InlineData(true,  "Review")]        // ephemeral review app: flag only
    public async Task ClearAll_OnADevSurface_StillClearsTheEnvironment(bool devTools, string environment)
    {
        var sut = Build(devToolsEnabled: devTools, environment: environment);

        var result = await sut.ClearAll();

        Assert.IsType<RedirectResult>(result);
        await _pageNodeRepository.Received(1).TruncateAllContentAsync();
    }

    // The view renders the reset section from this flag, so the button disappears with the route.
    [Theory]
    [InlineData(true,  "Development", true)]    // local dev
    [InlineData(false, "Development", true)]    // deployed DEV: no flag, still a dev environment
    [InlineData(true,  "Review",      true)]    // review app: flag set
    [InlineData(false, "QA",          false)]
    [InlineData(false, "Preproduction", false)]
    [InlineData(true,  "Production",  false)]
    public async Task Index_TellsTheView_WhetherClearAllIsAvailable(bool devTools, string environment, bool expected)
    {
        var sut = Build(devTools, environment);

        var result = Assert.IsType<ViewResult>(await sut.Index());

        Assert.Equal(expected, result.ViewData["ClearAllAvailable"]);
    }


    // The setting is the operator-facing switch, defaulted off, so an environment that MAY offer
    // clear-all still does not until somebody asks for it — and the hidden state can be exercised
    // without redeploying under different configuration.
    [Fact]
    public async Task ClearAll_WhenTheSettingIsOff_Returns404_EvenOnADevSurface()
    {
        var sut = Build(devToolsEnabled: true, environment: "Development", showButton: false);

        var result = await sut.ClearAll();

        Assert.IsType<NotFoundResult>(result);
        await _pageNodeRepository.DidNotReceiveWithAnyArgs().TruncateAllContentAsync();
    }

    [Fact]
    public async Task Index_WhenTheSettingIsOff_HidesTheButton_EvenOnADevSurface()
    {
        var sut = Build(devToolsEnabled: true, environment: "Development", showButton: false);

        var result = Assert.IsType<ViewResult>(await sut.Index());

        Assert.Equal(false, result.ViewData["ClearAllAvailable"]);
    }

    // The environment boundary is not editable: turning the setting on where the environment does
    // not allow it changes nothing. QA and preproduction are listed with dev tools off because
    // that is how they are configured — Dev:ToolsEnabled is the marker that declares an
    // environment a dev surface, so setting it on QA would be a statement that QA is one (and
    // would switch on dev impersonation besides). Production is refused with the flag ON, because
    // there the environment name alone has to be enough.
    [Theory]
    [InlineData("QA", false)]
    [InlineData("Preproduction", false)]
    [InlineData("Production", true)]
    public async Task ClearAll_SettingOn_StillRefusedWhereTheEnvironmentForbidsIt(string environment, bool devTools)
    {
        var sut = Build(devToolsEnabled: devTools, environment: environment, showButton: true);

        Assert.IsType<NotFoundResult>(await sut.ClearAll());
        Assert.Equal(false, Assert.IsType<ViewResult>(await sut.Index()).ViewData["ClearAllAvailable"]);
        await _pageNodeRepository.DidNotReceiveWithAnyArgs().TruncateAllContentAsync();
    }

    private static IFormFile FileFrom(string content) =>
        FileFrom(Encoding.UTF8.GetBytes(content), "bundle.json");

    private static IFormFile FileFrom(byte[] bytes, string fileName = "bundle.zip") =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "bundle", fileName);

    private static string ValidBundleJson() => ContentStagingJson.Serialize(new ContentBundle
    {
        PageNodes = [new() { Id = Guid.NewGuid(), Title = "Alpha", Segment = "alpha", PageType = "content" }]
    });

    [Fact]
    public void Controller_IsGatedByContentStagingSection()
    {
        var gate = typeof(ContentStagingController).GetCustomAttribute<RequireAdminSectionAttribute>();
        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.ContentStaging, gate!.SectionKey);
    }

    // The download is a zip now. Unwrap it the way the import side does, so these keep pinning
    // the bundle's contents rather than its transport.
    private static string ExportedJson(FileContentResult file) =>
        ContentBundleArchive.ReadBundleJson(file.FileContents, 50L * 1024 * 1024);

    [Fact]
    public async Task Export_ReturnsZipFile_StampedWithSchemaAndUser()
    {
        _staging.ExportAsync(Arg.Any<ContentExportSelection?>()).Returns(new ContentBundle
        {
            PageNodes = [new() { Id = Guid.NewGuid(), Title = "Alpha", Segment = "alpha", PageType = "content" }]
        });

        var result = await _sut.Export();

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/zip", file.ContentType);
        Assert.EndsWith(".zip", file.FileDownloadName);
        var json = ExportedJson(file);
        Assert.Contains(ContentBundle.CurrentSchema, json);
        Assert.Contains("editor@education.gov.uk", json);
    }

    [Fact]
    public async Task Export_StampsSchemaVersion()
    {
        _staging.ExportAsync(Arg.Any<ContentExportSelection?>()).Returns(new ContentBundle
        {
            PageNodes = [new() { Id = Guid.NewGuid(), Title = "Alpha", Segment = "alpha", PageType = "content" }]
        });

        var result = await _sut.Export();

        var json = ExportedJson(Assert.IsType<FileContentResult>(result));
        Assert.Contains("schemaVersion", json);
        var roundTripped = ContentStagingJson.Deserialize(json)!;
        Assert.Equal(ContentBundle.CurrentSchemaVersion, roundTripped.SchemaVersion);
    }

    // The pair that matters most: what Export hands out is exactly what Preview accepts back.
    [Fact]
    public async Task Export_ThenPreview_AcceptsItsOwnZippedBundle()
    {
        _staging.ExportAsync(Arg.Any<ContentExportSelection?>()).Returns(new ContentBundle
        {
            PageNodes = [new() { Id = Guid.NewGuid(), Title = "Alpha", Segment = "alpha", PageType = "content" }]
        });
        _staging.PreviewAsync(Arg.Any<ContentBundle>()).Returns(new ContentImportPreview([], []));
        var exported = Assert.IsType<FileContentResult>(await _sut.Export());

        var result = await _sut.Preview(FileFrom(exported.FileContents));

        Assert.IsType<ViewResult>(result);
        await _staging.Received(1).PreviewAsync(Arg.Is<ContentBundle>(b => b.PageNodes.Count == 1));
    }

    // Bundles downloaded from earlier releases are plain JSON and must keep importing.
    [Fact]
    public async Task Preview_StillAcceptsAPlainJsonBundle()
    {
        _staging.PreviewAsync(Arg.Any<ContentBundle>()).Returns(new ContentImportPreview([], []));

        var result = await _sut.Preview(FileFrom(ValidBundleJson()));

        Assert.IsType<ViewResult>(result);
        await _staging.Received(1).PreviewAsync(Arg.Any<ContentBundle>());
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
            PageNodes = [new() { Id = pageId, Title = "Alpha", Segment = "alpha", PageType = "content" }]
        });

        var result = await _sut.ExportSelected([pageId], [blockId]);

        Assert.IsType<FileContentResult>(result);
        await _staging.Received(1).ExportAsync(Arg.Is<ContentExportSelection>(
            s => s.PageNodeIds.Contains(pageId) && s.ContentBlockIds.Contains(blockId)));
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

    // ── Export version-history controls ────────────────────────────────────

    // The whole-environment export asks for everything (null id sets) and carries only the
    // history preference — an empty set would mean "no pages at all".
    [Fact]
    public async Task Export_ByDefault_RequestsEverything_WithTheHistoryCap()
    {
        _staging.ExportAsync(Arg.Any<ContentExportSelection?>()).Returns(new ContentBundle());

        await _sut.Export();

        await _staging.Received(1).ExportAsync(Arg.Is<ContentExportSelection?>(s =>
            s!.PageNodeIds == null
            && s.ContentBlockIds == null
            && s.MaxVersionsPerNode == ContentExportSelection.DefaultMaxVersionsPerNode));
    }

    [Fact]
    public async Task Export_WithFullHistory_LiftsTheCap()
    {
        _staging.ExportAsync(Arg.Any<ContentExportSelection?>()).Returns(new ContentBundle());

        await _sut.Export(fullHistory: true);

        await _staging.Received(1).ExportAsync(
            Arg.Is<ContentExportSelection?>(s => s!.MaxVersionsPerNode == null));
    }

    [Fact]
    public async Task ExportSelected_CarriesTheHistoryPreference_AlongsideTheTickedIds()
    {
        var pageId = Guid.NewGuid();
        _staging.ExportAsync(Arg.Any<ContentExportSelection?>()).Returns(new ContentBundle());

        await _sut.ExportSelected([pageId], null, fullHistory: true);

        await _staging.Received(1).ExportAsync(Arg.Is<ContentExportSelection?>(s =>
            s!.PageNodeIds!.Contains(pageId) && s.MaxVersionsPerNode == null));
    }

    [Fact]
    public async Task ExportSelected_ByDefault_AppliesTheHistoryCap()
    {
        var pageId = Guid.NewGuid();
        _staging.ExportAsync(Arg.Any<ContentExportSelection?>()).Returns(new ContentBundle());

        await _sut.ExportSelected([pageId], null);

        await _staging.Received(1).ExportAsync(Arg.Is<ContentExportSelection?>(s =>
            s!.MaxVersionsPerNode == ContentExportSelection.DefaultMaxVersionsPerNode));
    }

    // ── Server-side preview session ─────────────────────────────────────────

    // The bundle stays on the server between Preview and Import. The view model carries the
    // session id and nothing else — if the bundle ever leaks back into the page the round-trip
    // (and the request-size ceiling it forced) is back.
    [Fact]
    public async Task Preview_ValidFile_StoresBundleInSession_AndReturnsOnlyTheSessionId()
    {
        var sessionId = Guid.NewGuid();
        _staging.PreviewAsync(Arg.Any<ContentBundle>()).Returns(new ContentImportPreview([], []));
        _sessions.CreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(sessionId);

        var result = await _sut.Preview(FileFrom(ValidBundleJson()));

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ImportPreviewViewModel>(view.Model);
        Assert.Equal(sessionId, model.SessionId);
        await _sessions.Received(1).CreateAsync(
            Arg.Is<string>(j => j.Contains(ContentBundle.CurrentSchema)),
            "editor@education.gov.uk",
            Arg.Any<CancellationToken>());
        await _staging.Received(1).PreviewAsync(Arg.Is<ContentBundle>(b => b.PageNodes.Count == 1));
    }

    // Creating a session is the natural moment to drop the ones nobody came back for — it needs
    // no scheduler, and a table that only ever holds in-flight previews cannot grow without
    // somebody starting a new one.
    [Fact]
    public async Task Preview_ValidFile_PurgesExpiredSessions()
    {
        _staging.PreviewAsync(Arg.Any<ContentBundle>()).Returns(new ContentImportPreview([], []));

        await _sut.Preview(FileFrom(ValidBundleJson()));

        await _sessions.Received(1).PurgeExpiredAsync(Arg.Any<CancellationToken>());
    }

    // An id with no live session behind it — expired, already imported, or simply made up.
    [Fact]
    public async Task Import_UnknownSession_SetsExpiredError_AndImportsNothing()
    {
        var result = await _sut.Import(new ImportConfirmFormModel { SessionId = Guid.NewGuid() });

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging", redirect.Url);
        Assert.Contains("expired", _sut.TempData["ContentStagingError"]!.ToString()!);
        await _staging.DidNotReceiveWithAnyArgs().ImportAsync(default!, default);
    }

    // A spent session is a used ticket: the bundle has landed, so the row goes.
    [Fact]
    public async Task Import_Successful_DeletesTheSession()
    {
        var sessionId = SessionWith(ValidBundleJson());
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns(new ContentImportResult { PageNodesCreated = 1 });

        await _sut.Import(new ImportConfirmFormModel { SessionId = sessionId });

        await _sessions.Received(1).DeleteAsync(sessionId, Arg.Any<CancellationToken>());
    }

    // A per-item failure leaves part of the bundle unapplied, so the session has to survive for
    // the same reason a thrown import does — the operator fixes the cause and confirms again.
    [Fact]
    public async Task Import_WithPerItemErrors_KeepsTheSessionForRetry()
    {
        var sessionId = SessionWith(ValidBundleJson());
        var partial = new ContentImportResult { PageNodesCreated = 1 };
        partial.Errors.Add("Skipped 'a/b' — parent not found.");
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns(partial);

        await _sut.Import(new ImportConfirmFormModel { SessionId = sessionId });

        await _sessions.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // An audit-write failure must not be reported as an import failure: the import has already
    // been applied by then, and saying otherwise invites a re-run.
    [Fact]
    public async Task Import_WhenTheAuditWriteFails_StillReportsSuccess()
    {
        var (dbContext, _) = SubstituteDbContext();
        dbContext.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("audit table unavailable"));
        var sut = NewSutWith(dbContext);
        var sessionId = SessionWith(ValidBundleJson());
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns(new ContentImportResult { PageNodesCreated = 1 });

        await sut.Import(new ImportConfirmFormModel { SessionId = sessionId });

        Assert.NotNull(sut.TempData["ContentStagingResult"]);
        Assert.Null(sut.TempData["ContentStagingError"]);
    }

    // A long list of validation issues goes into cookie-backed TempData and comes back up as
    // request headers on the redirect, so it has to be bounded or one junk upload 431s the
    // browser until its cookies are cleared.
    [Fact]
    public async Task Import_WithVeryManyErrors_TruncatesTheBanner()
    {
        var sessionId = SessionWith(ValidBundleJson());
        var noisy = new ContentImportResult();
        for (var i = 0; i < 500; i++) noisy.Errors.Add($"Item {i} failed for some long-winded reason.");
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns(noisy);

        await _sut.Import(new ImportConfirmFormModel { SessionId = sessionId });

        var banner = _sut.TempData["ContentStagingError"] as string;
        Assert.NotNull(banner);
        Assert.Contains("and 480 more", banner);
        Assert.True(banner!.Length < 4096, $"banner was {banner.Length} chars — too big for a cookie");
    }

    // A failed import keeps its session, so the operator can fix the cause and confirm again
    // without re-uploading a bundle that may have taken minutes to transfer.
    [Fact]
    public async Task Import_WhenServiceThrows_KeepsTheSessionForRetry()
    {
        var sessionId = SessionWith(ValidBundleJson());
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns<ContentImportResult>(_ => throw new InvalidOperationException("boom"));

        await _sut.Import(new ImportConfirmFormModel { SessionId = sessionId });

        await _sessions.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── Upload size cap ────────────────────────────────────────────────────

    // A bundle over the 50 MB cap is rejected before the JSON parser sees a byte. Uses a
    // stubbed IFormFile whose Length reports the oversized value without actually
    // allocating that many bytes — proving the guard reads Length, not the stream.
    [Fact]
    public async Task Preview_FileOverSizeCap_SetsError_AndRedirects_WithoutParsing()
    {
        var oversized = Substitute.For<IFormFile>();
        oversized.Length.Returns(51L * 1024 * 1024);   // 51 MB → over the 50 MB cap
        oversized.OpenReadStream().Returns(new MemoryStream());

        var result = await _sut.Preview(oversized);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging", redirect.Url);
        Assert.NotNull(_sut.TempData["ContentStagingError"]);
        Assert.Contains("too large", _sut.TempData["ContentStagingError"]!.ToString()!);
        await _staging.DidNotReceive().PreviewAsync(Arg.Any<ContentBundle>());
        oversized.DidNotReceive().OpenReadStream();
    }

    // ── Strict UTF-8 ────────────────────────────────────────────────────────

    // An invalid UTF-8 byte in the middle of otherwise-valid JSON is rejected with a
    // specific error banner — previously the default StreamReader silently substituted
    // U+FFFD, which would corrupt strings and could round-trip into the DB unnoticed.
    [Fact]
    public async Task Preview_InvalidUtf8_SetsError_AndRedirects()
    {
        var invalidUtf8Bytes = new byte[] { 0xFF, 0xFE, 0xFD, 0x7B, 0x7D };  // lone 0xFF etc. is invalid UTF-8
        var file = new FormFile(new MemoryStream(invalidUtf8Bytes), 0, invalidUtf8Bytes.Length, "bundle", "bundle.json");

        var result = await _sut.Preview(file);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.NotNull(_sut.TempData["ContentStagingError"]);
        Assert.Contains("invalid UTF-8", _sut.TempData["ContentStagingError"]!.ToString()!);
        await _staging.DidNotReceive().PreviewAsync(Arg.Any<ContentBundle>());
    }

    // ── Validator surfacing ─────────────────────────────────────────────────

    [Fact]
    public async Task Preview_WhenServiceThrowsValidationException_SurfacesIssuesAsError()
    {
        _staging.PreviewAsync(Arg.Any<ContentBundle>())
            .Returns<ContentImportPreview>(_ =>
                throw new ContentImportValidationException(new List<ValidationIssue>
                {
                    new(ValidationSeverity.Fatal, "SEGMENT_INVALID", "Page 'Help' has invalid Segment 'HELP'."),
                    new(ValidationSeverity.Fatal, "PAGE_TYPE_UNKNOWN", "Page 'Help' has unknown PageType 'foo'."),
                }));

        var result = await _sut.Preview(FileFrom(ValidBundleJson()));

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging", redirect.Url);
        var banner = _sut.TempData["ContentStagingError"]!.ToString()!;
        Assert.Contains("failed validation", banner);
        Assert.Contains("Segment 'HELP'", banner);
        Assert.Contains("PageType 'foo'", banner);
    }

    [Fact]
    public async Task Import_NoSessionId_SetsError_AndRedirects()
    {
        var result = await _sut.Import(new ImportConfirmFormModel { SessionId = Guid.Empty });

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
            .Returns(new ContentImportResult { PageNodesUpdated = 1 });

        var model = new ImportConfirmFormModel
        {
            SessionId = SessionWith(ValidBundleJson()),
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
            SessionId = SessionWith(ValidBundleJson()),
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
            SessionId = SessionWith(ValidBundleJson()),
            GlobalMode = ContentImportMode.Fail
        });

        Assert.IsType<RedirectResult>(result);
        Assert.Equal("clash", _sut.TempData["ContentStagingError"]);
    }

    // /import is POST-only in the actual flow, but bookmarks/pasted URLs shouldn't fall
    // through to the catch-all PageController — which produces a 404 locally and a 500 on
    // some review-app configurations. A GET should just bounce back to the landing page.
    [Fact]
    public void ImportLanding_GET_RedirectsToStagingHome()
    {
        var result = _sut.ImportLanding();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging", redirect.Url);
    }

    // Destructive wipe: hits the repository truncate and lands the user back on the landing
    // page with a success banner. Guarded by CSRF + editor role at the framework level.
    [Fact]
    public async Task ClearAll_TruncatesRepository_AndRedirectsWithSuccessBanner()
    {
        var result = await _sut.ClearAll();

        await _pageNodeRepository.Received(1).TruncateAllContentAsync();
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging", redirect.Url);
        Assert.NotNull(_sut.TempData["ContentStagingResult"]);
    }

    [Fact]
    public void ClearAll_HasValidateAntiForgeryTokenAndHttpPost()
    {
        var method = typeof(ContentStagingController)
            .GetMethod(nameof(ContentStagingController.ClearAll))!;
        Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>());
    }

    // ── Audit logging on Import ──────────────────────────────────────────────
    //
    // Every destructive admin action in the codebase writes an AuditEntry (see
    // QueueAdminController.WriteActionAuditAsync). Content-staging Import is a
    // bulk destructive action that touches thousands of rows — and until now
    // wrote zero audit rows. Pin the "one AuditEntry per successful import"
    // contract with the summary shape stored in NewValues.

    [Fact]
    public async Task Import_Confirm_Successful_WritesAuditEntry_WithBundleSummary()
    {
        var (dbContext, audits) = SubstituteDbContext();
        _currentUser.UserId.Returns("editor@education.gov.uk");
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns(new ContentImportResult
            {
                PageNodesCreated = 3, PageNodesUpdated = 1, PageNodesSkipped = 0,
                ContentBlocksCreated = 5, ContentBlocksUpdated = 2, ContentBlocksSkipped = 1,
                Warnings = { "Sanitised HTML in 2 bundle item(s)..." },
            });
        var sut = NewSutWith(dbContext);

        await sut.Import(new ImportConfirmFormModel
        {
            SessionId = SessionWith(ValidBundleJson()),
            GlobalMode = ContentImportMode.Replace,
        });

        audits.Received(1).Add(Arg.Is<AuditEntry>(a =>
            a.EntityType == "ContentBundle" &&
            a.EntityId == "import" &&
            a.Action == "Import" &&
            a.UserId == "editor@education.gov.uk" &&
            a.NewValues != null &&
            a.NewValues.Contains("\"PageNodesCreated\":3") &&
            a.NewValues.Contains("\"ContentBlocksCreated\":5") &&
            a.NewValues.Contains("\"WarningCount\":1")));
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_Confirm_WhenServiceThrows_DoesNotWriteAuditEntry()
    {
        var (dbContext, audits) = SubstituteDbContext();
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns<ContentImportResult>(_ =>
                throw new InvalidOperationException("db-blew-up"));
        var sut = NewSutWith(dbContext);

        await sut.Import(new ImportConfirmFormModel
        {
            SessionId = SessionWith(ValidBundleJson()),
            GlobalMode = ContentImportMode.Replace,
        });

        // Failed import means no audit — we only record successful mutations to keep the
        // audit trail a true record of "what changed", not "what was attempted".
        audits.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Import_Confirm_WhenDbContextIsNull_StillReturnsSuccess()
    {
        // Existing test-set constructs the controller without a db context; the audit
        // write must gracefully no-op rather than NRE. Belt-and-braces mirror of the
        // same pattern in QueueAdminController.
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns(new ContentImportResult { PageNodesCreated = 1 });

        var result = await _sut.Import(new ImportConfirmFormModel
        {
            SessionId = SessionWith(ValidBundleJson()),
            GlobalMode = ContentImportMode.Replace,
        });

        Assert.IsType<RedirectResult>(result);
        Assert.NotNull(_sut.TempData["ContentStagingResult"]);
    }

    private ContentStagingController NewSutWith(IPortalDbContext dbContext, IContentStagingLock? importLock = null)
    {
        return Build(devToolsEnabled: true, environment: "Development", dbContext: dbContext, importLock: importLock);
    }

    // ── Concurrent-import advisory lock ──────────────────────────────────────

    [Fact]
    public async Task Import_Confirm_WhenLockUnavailable_SkipsImport_AndSurfacesBanner()
    {
        var (dbContext, _) = SubstituteDbContext();
        var importLock = Substitute.For<IContentStagingLock>();
        importLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(false);
        var sut = NewSutWith(dbContext, importLock);

        var result = await sut.Import(new ImportConfirmFormModel
        {
            SessionId = SessionWith(ValidBundleJson()),
            GlobalMode = ContentImportMode.Replace,
        });

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging", redirect.Url);
        Assert.Contains("Another import", sut.TempData["ContentStagingError"]?.ToString() ?? "");
        // Import path must have been short-circuited before touching the service.
        await _staging.DidNotReceiveWithAnyArgs().ImportAsync(default!, default, default!, default);
        // And no attempt to release a lock we never acquired.
        await importLock.DidNotReceive().ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_Confirm_WhenLockAcquired_ReleasesInFinally_EvenOnFailure()
    {
        var (dbContext, _) = SubstituteDbContext();
        var importLock = Substitute.For<IContentStagingLock>();
        importLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(true);
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns<ContentImportResult>(_ =>
                throw new InvalidOperationException("db-blew-up"));
        var sut = NewSutWith(dbContext, importLock);

        await sut.Import(new ImportConfirmFormModel
        {
            SessionId = SessionWith(ValidBundleJson()),
            GlobalMode = ContentImportMode.Replace,
        });

        // Release must still fire — otherwise a crashed import leaks the lock and every
        // subsequent import gets rejected until the DB session recycles.
        await importLock.Received(1).ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_Confirm_WhenLockAcquired_ReleasesAfterSuccess()
    {
        var (dbContext, _) = SubstituteDbContext();
        var importLock = Substitute.For<IContentStagingLock>();
        importLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(true);
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns(new ContentImportResult { PageNodesCreated = 1 });
        var sut = NewSutWith(dbContext, importLock);

        await sut.Import(new ImportConfirmFormModel
        {
            SessionId = SessionWith(ValidBundleJson()),
            GlobalMode = ContentImportMode.Replace,
        });

        await importLock.Received(1).TryAcquireAsync(Arg.Any<CancellationToken>());
        await importLock.Received(1).ReleaseAsync(Arg.Any<CancellationToken>());
    }

    private static (IPortalDbContext DbContext, DbSet<AuditEntry> Audits) SubstituteDbContext()
    {
        var dbContext = Substitute.For<IPortalDbContext>();
        var audits = Substitute.For<DbSet<AuditEntry>>();
        dbContext.AuditEntries.Returns(audits);
        dbContext.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        return (dbContext, audits);
    }

    // The upload endpoint must reject requests with the wrong Content-Type early in the
    // pipeline rather than accepting them and failing later. Pin the [Consumes] contract
    // so a future refactor can't drop it.
    [Fact]
    public void Preview_OnlyConsumesMultipartFormData()
    {
        var method = typeof(ContentStagingController).GetMethod(nameof(ContentStagingController.Preview))!;
        var consumes = method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ConsumesAttribute>();
        Assert.NotNull(consumes);
        Assert.Contains("multipart/form-data", consumes!.ContentTypes);
    }

    // Same shape for Import: takes a form-urlencoded body (session id + collision mode
    // radios); reject anything else.
    [Fact]
    public void Import_OnlyConsumesFormUrlEncoded()
    {
        var method = typeof(ContentStagingController).GetMethod(nameof(ContentStagingController.Import))!;
        var consumes = method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ConsumesAttribute>();
        Assert.NotNull(consumes);
        Assert.Contains("application/x-www-form-urlencoded", consumes!.ContentTypes);
    }

    // An unexpected exception (e.g. DB error) inside ImportAsync used to bubble to a bare 500.
    // Now it's caught, logged, and surfaced to the user as a coherent error banner.
    [Fact]
    public async Task Import_Confirm_UnexpectedException_IsCaught_LoggedAndSurfacedAsError()
    {
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns<ContentImportResult>(_ =>
                throw new InvalidOperationException("db-blew-up"));

        var result = await _sut.Import(new ImportConfirmFormModel
        {
            SessionId = SessionWith(ValidBundleJson()),
            GlobalMode = ContentImportMode.Skip
        });

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/content-staging", redirect.Url);
        var error = _sut.TempData["ContentStagingError"] as string;
        Assert.NotNull(error);
        Assert.Contains("InvalidOperationException", error);
        Assert.Contains("db-blew-up", error);
    }

    [Fact]
    public async Task Import_Confirm_WithWarnings_ExposesThemInTempData()
    {
        var withWarning = new ContentImportResult { PageNodesSkipped = 1 };
        withWarning.Warnings.Add("Skipped 'x/y' — parent 'x' not found.");
        _staging.ImportAsync(Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>>())
            .Returns(withWarning);

        await _sut.Import(new ImportConfirmFormModel { SessionId = SessionWith(ValidBundleJson()) });

        Assert.NotNull(_sut.TempData["ContentStagingWarnings"]);
    }
}
