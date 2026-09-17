using System.Security.Claims;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.Admin;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.WindowAdmin;

public sealed class CheckingExerciseCreationPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private PortalDbContext Context() => new(new DbContextOptionsBuilder<PortalDbContext>()
        .UseNpgsql(_postgres.GetConnectionString()).Options, new FakeCurrentUserService());

    [Theory]
    [InlineData(null, false)]
    [InlineData("Pupil", false)]
    [InlineData("ValueAdded", true)]
    public async Task Migration_removes_redundant_columns_and_preserves_exercises_and_inputs(string? oldDataType, bool requiresValidation)
    {
        await using var db = Context();
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        var now = new DateTime(2026, 9, 15);
        var exercise = new CheckingExercise
        {
            Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData,
            Name = "Revised students", TabName = "Revised students", StartDate = now, EndDate = now.AddDays(1),
            Validated = new() { ValidatedAt = DateTime.SpecifyKind(now, DateTimeKind.Utc), IngressValidationChecksum = "synthetic", SchemaValidationChecksum = "synthetic" },
            Datasets = [new() { Name = "input", IngressFile = "input.csv", SchemaFile = "schema.json" }]
        };
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(), Title = "Migration example", CheckingWindowType = CheckingWindowType.Post16,
            KeyStage = KeyStages.Post16, StartDate = now, EndDate = now.AddDays(1), CheckingExercises = [exercise]
        };
        db.CheckingWindows.Add(window);
        await db.SaveChangesAsync();
        await migrator.MigrateAsync("20260914150016_ProtectLegacyExerciseStorage");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "CheckingExercises" SET "Stage" = 'Revised', "DataType" = {oldDataType} WHERE "Id" = {exercise.Id}
            """);
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        var columns = await db.Database.SqlQueryRaw<string>("""
            SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name = 'CheckingExercises'
            """).ToListAsync();
        Assert.DoesNotContain("Stage", columns);
        Assert.DoesNotContain("DataType", columns);
        var saved = await db.Set<CheckingExercise>().Include(e => e.Datasets).SingleAsync(e => e.Id == exercise.Id);
        Assert.Equal(window.Id, saved.CheckingWindowId);
        Assert.Equal("Revised students", saved.Name);
        Assert.Equal("Revised students", saved.TabName);
        Assert.Equal(requiresValidation, saved.Validated is null);
        var input = Assert.Single(saved.Datasets);
        Assert.Equal("input.csv", input.IngressFile);
        Assert.Equal("schema.json", input.SchemaFile);
        Assert.Equal(saved.Id, input.CheckingExerciseId);
    }

    [Theory]
    [InlineData(CheckingWindowType.Post16, 2)]
    [InlineData(CheckingWindowType.KS4June, 1)]
    [InlineData(CheckingWindowType.KS2, 1)]
    public async Task Creation_round_trips_metadata_and_preserves_other_exercises_and_windows(
        CheckingWindowType type, int datasetCount)
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(2), DateTimeKind.Unspecified);
        await using var db = Context();
        var parent = new CheckingWindow
        {
            Id = Guid.NewGuid(), Title = "Pupil data checking", CheckingWindowType = type,
            KeyStage = type == CheckingWindowType.Post16 ? KeyStages.Post16 : KeyStages.KS4,
            StartDate = now, EndDate = now.AddMonths(1),
            CheckingExercises = [new CheckingExercise
            {
                Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData,
                Name = "Provisional", StartDate = now, EndDate = now.AddDays(3),
                Datasets = [new CheckingWindowDataset { Name = "custom-input", IngressFile = "existing.csv", SchemaFile = "existing.json" }]
            }]
        };
        var other = new CheckingWindow
        {
            Id = Guid.NewGuid(), Title = "Other window", CheckingWindowType = type, KeyStage = parent.KeyStage,
            StartDate = now, EndDate = now.AddDays(4)
        };
        db.CheckingWindows.AddRange(parent, other);
        await db.SaveChangesAsync();
        var previous = Assert.Single(parent.CheckingExercises);
        var oldDatasetId = previous.Datasets[0].Id;
        db.ChangeTracker.Clear();
        var service = new WindowService(new WindowRepository(db), TimeProvider.System);
        var urls = Substitute.For<IUrlHelper>();
        urls.Action(Arg.Any<UrlActionContext>()).Returns("/summary");
        var controller = new CreateCheckingExerciseController(service, TimeProvider.System)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }, Url = urls
        };

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.Submit(parent.Id, new()
        {
            WindowId = parent.Id, Name = "Revised students",
            ExerciseType = CheckingExerciseType.PupilData,
            TabName = "Students", TabOrder = 2, SortOrder = 3, IsEnabled = true, DisplayOnly = true,
            VisibleFrom = now, VisibleUntil = now.AddMonths(2), ReplacesCheckingExerciseId = previous.Id,
            Dates = new() { StartDate = now.AddDays(5), StartHour = 9, EndDate = now.AddDays(10), EndHour = 17 }
        }, default));
        Assert.Equal(parent.Id, redirect.RouteValues!["id"]);
        db.ChangeTracker.Clear();

        var saved = await db.CheckingWindows.Include(w => w.CheckingExercises).ThenInclude(e => e.Datasets)
            .SingleAsync(w => w.Id == parent.Id);
        Assert.Equal(2, saved.CheckingExercises.Count);
        var created = Assert.Single(saved.CheckingExercises, e => e.Id != previous.Id);
        Assert.Equal(parent.Id, created.CheckingWindowId);
        Assert.Equal("Revised students", created.Name);
        Assert.Equal("Students", created.TabName);
        Assert.Equal(2, created.TabOrder);
        Assert.Equal(3, created.SortOrder);
        Assert.True(created.IsEnabled);
        Assert.True(created.DisplayOnly);
        Assert.True(created.UsesExerciseStorage);
        Assert.Equal(previous.Id, created.ReplacesCheckingExerciseId);
        Assert.Equal(now, created.VisibleFrom);
        Assert.Equal(now.AddMonths(2), created.VisibleUntil);
        Assert.Equal(now.AddDays(5).AddHours(9), created.StartDate);
        Assert.Equal(now.AddDays(10).AddHours(17), created.EndDate);
        Assert.Null(created.Validated);
        Assert.Equal(datasetCount, created.Datasets.Count);
        Assert.All(created.Datasets, d => Assert.Equal(created.Id, d.CheckingExerciseId));
        Assert.Equal(now, saved.StartDate);
        Assert.Equal(now.AddMonths(1), saved.EndDate);
        var unchanged = saved.CheckingExercises.Single(e => e.Id == previous.Id);
        Assert.Equal("Provisional", unchanged.Name);
        Assert.Equal(oldDatasetId, Assert.Single(unchanged.Datasets).Id);
        Assert.Equal("existing.csv", unchanged.Datasets[0].IngressFile);
        Assert.Empty(await db.Set<CheckingExercise>().Where(e => e.CheckingWindowId == other.Id).ToListAsync());

        var summary = Assert.IsType<WindowEditItem>(
            Assert.IsType<ViewResult>(await new SummaryController(service).Index(parent.Id, default)).Model);
        Assert.Contains(summary.Exercises, e => e.ExerciseId == created.Id && e.Label == "Revised students");

        var editController = new EditCheckingExerciseController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }, Url = urls
        };
        var edit = Assert.IsType<CreateCheckingExerciseItem>(
            Assert.IsType<ViewResult>(await editController.Edit(parent.Id, created.Id, default)).Model);
        edit.Name = "Edited exercise";
        edit.ExerciseType = CheckingExerciseType.ResultsEnquiry;
        edit.TabName = "Results";
        edit.TabOrder = 4;
        edit.SortOrder = 6;
        Assert.True(edit.DisplayOnly);
        edit.DisplayOnly = false;
        edit.IsEnabled = false;
        edit.VisibleFrom = now.AddHours(9);
        edit.VisibleUntil = now.AddDays(20).AddHours(17);
        edit.Dates.EndDate = now.AddDays(15);
        Assert.IsType<RedirectToActionResult>(await editController.Update(parent.Id, created.Id, edit, default));
        db.ChangeTracker.Clear();
        var edited = await db.Set<CheckingExercise>().Include(e => e.Datasets).SingleAsync(e => e.Id == created.Id);
        Assert.Equal("Edited exercise", edited.Name);
        Assert.Equal(parent.Id, edited.CheckingWindowId);
        Assert.Equal(CheckingExerciseType.ResultsEnquiry, edited.ExerciseType);
        Assert.Equal("Results", edited.TabName);
        Assert.Equal(4, edited.TabOrder);
        Assert.Equal(6, edited.SortOrder);
        Assert.False(edited.IsEnabled);
        Assert.False(edited.DisplayOnly);
        Assert.Equal(edit.VisibleFrom, edited.VisibleFrom);
        Assert.Equal(edit.VisibleUntil, edited.VisibleUntil);
        Assert.Equal(edit.Dates.EndDateTime, edited.EndDate);
        Assert.Equal(previous.Id, edited.ReplacesCheckingExerciseId);
        Assert.Equal(created.Datasets.Select(d => d.Id).Order(), edited.Datasets.Select(d => d.Id).Order());
        Assert.Equal("Provisional", (await db.Set<CheckingExercise>().SingleAsync(e => e.Id == previous.Id)).Name);
        Assert.Equal(2, await db.Set<CheckingExercise>().CountAsync(e => e.CheckingWindowId == parent.Id));

        var dataController = new ExerciseDataController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }, Url = urls
        };
        Assert.IsType<RedirectToActionResult>(await dataController.Submit(parent.Id, created.Id, new()
        {
            WindowId = parent.Id, Name = "additional-input", Inclusion = "excluded", Required = true
        }, default));
        db.ChangeTracker.Clear();
        var pair = await db.Set<CheckingWindowDataset>()
            .SingleAsync(d => d.CheckingExerciseId == created.Id && d.Name == "additional-input");
        Assert.NotEqual(Guid.Empty, pair.Id);
        Assert.False(pair.Included);
        Assert.True(pair.Required);
        Assert.Equal(datasetCount + 1, await db.Set<CheckingWindowDataset>().CountAsync(d => d.CheckingExerciseId == created.Id));
        Assert.Single(await db.Set<CheckingWindowDataset>().Where(d => d.CheckingExerciseId == previous.Id).ToListAsync());
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Untyped_exercises_require_display_only_and_exercise_scoped_storage(bool displayOnly, bool usesExerciseStorage)
    {
        await using var db = Context();
        var now = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
        var exercise = new CheckingExercise
        {
            Id = Guid.NewGuid(), Name = "Summary", TabName = "Summary", ExerciseType = null,
            DisplayOnly = displayOnly, UsesExerciseStorage = usesExerciseStorage,
            StartDate = now, EndDate = now.AddDays(1)
        };
        db.CheckingWindows.Add(new CheckingWindow
        {
            Id = Guid.NewGuid(), Title = "Summary", CheckingWindowType = CheckingWindowType.Post16,
            KeyStage = KeyStages.Post16, StartDate = now, EndDate = now.AddDays(1), CheckingExercises = [exercise]
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgresError = Assert.IsType<Npgsql.PostgresException>(error.InnerException);
        Assert.Equal("CK_CheckingExercises_TypeOrDisplayOnly", postgresError.ConstraintName);
    }

}

public sealed class CheckingExerciseAdminRenderTests
{
    [Fact]
    public async Task Summary_renders_empty_state_and_parent_scoped_add_action()
    {
        var model = Model();
        var html = await Render("Summary", model);
        Assert.Contains("There are no checking exercises in this window", html);
        Assert.Contains($"href=\"/admin/windows/{model.WindowId}/exercises/new\"", html);
        Assert.Contains("Add checking exercise", html);
        Assert.Contains("Window details", html);
    }

    [Fact]
    public async Task Summary_renders_table_and_preserves_file_sections()
    {
        var model = Model();
        var id = Guid.NewGuid();
        model.Exercises = [new()
        {
            WindowId = model.WindowId, ExerciseId = id, ExerciseType = CheckingExerciseType.PupilData,
            Label = "Revised students", TabName = "Students",
            StartDate = model.StartDate, EndDate = model.EndDate, IsEnabled = true,
            Datasets = [new()
            {
                WindowId = model.WindowId, ExerciseId = id, Exercise = CheckingExerciseType.PupilData,
                Name = "pupils", Label = "Pupils", IngressFile = "example.csv", SchemaFile = "example.json"
            }]
        }];
        var html = await Render("Summary", model);
        Assert.Contains("govuk-table", html);
        Assert.Contains("Checking exercises for Pupil data checking", html);
        Assert.Contains($"href=\"#exercise-{id}\"", html);
        Assert.Contains($"id=\"exercise-{id}\"", html);
        Assert.Contains("Students", html);
        Assert.Contains("example.csv", html);
        Assert.Contains("example.json", html);
        Assert.Contains("Validate Revised students", html);
        Assert.Contains($"exerciseId={id}", html);
        Assert.Contains($"href=\"/admin/windows/{model.WindowId}/exercises/{id}/edit\"", html);
        Assert.DoesNotContain(">Enabled</th>", html);
    }

    [Fact]
    public async Task Creation_form_renders_fields_antiforgery_and_field_linked_errors()
    {
        var model = new CreateCheckingExerciseItem
        {
            WindowId = Guid.NewGuid(), WindowTitle = "Pupil data checking", Name = "Entered name",
            PostUrl = "/post", CancelUrl = "/summary", IsEnabled = true
        };
        var errors = new ModelStateDictionary();
        errors.AddModelError("Name", "Enter an exercise name");
        errors.AddModelError("Dates.StartDate", "Enter a real start date");
        errors.AddModelError("ExerciseType", "Select an exercise type");
        var html = await Render("CreateCheckingExercise", model, errors);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("Entered name", html);
        Assert.Matches("""<input[^>]*name="ExerciseType"[^>]*value=""[^>]*checked[^>]*>""", html);
        Assert.Contains("There is a problem", html);
        Assert.Contains("Enter a real start date", html);
        foreach (var field in new[] { "Name", "ExerciseType", "TabName", "TabOrder", "SortOrder", "DisplayOnly", "ReplacesCheckingExerciseId" })
            Assert.Contains($"name=\"{field}\"", html);
        // Error-summary links must target elements that exist in the rendered GOV.UK controls.
        foreach (Match match in Regex.Matches(html, "href=\"#([^\"]+)\""))
            Assert.Contains($"id=\"{match.Groups[1].Value}\"", html);
        Assert.DoesNotContain("name=\"DataType\"", html);
        Assert.DoesNotContain("name=\"Stage\"", html);
        Assert.DoesNotContain("Output data type", html);
        Assert.DoesNotContain("<govuk-", html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Edit_form_shows_saved_values_and_save_action(bool enabled)
    {
        var model = new CreateCheckingExerciseItem
        {
            WindowId = Guid.NewGuid(), IsEditing = true, WindowTitle = "Pupil data checking",
            Name = "Saved exercise", PostUrl = "/exercise/edit", CancelUrl = "/summary",
            ExerciseType = CheckingExerciseType.ResultsEnquiry,
            TabName = "Results", IsEnabled = enabled, VisibleFrom = new DateTime(2026, 9, 15, 9, 30, 15)
        };
        var html = await Render("CreateCheckingExercise", model);
        Assert.Contains("Edit checking exercise", html);
        Assert.Contains("Save changes", html);
        Assert.Contains("Saved exercise", html);
        Assert.Contains("action=\"/exercise/edit\"", html);
        Assert.Contains("2026-09-15T09:30:15", html);
        var checkbox = Regex.Match(html, """<input(?=[^>]*name="IsEnabled")[^>]*>""").Value;
        Assert.NotEmpty(checkbox);
        Assert.Equal(enabled, checkbox.Contains("checked"));
        Assert.Contains("checked", Regex.Match(html, """<input(?=[^>]*name="ExerciseType")(?=[^>]*value="ResultsEnquiry")[^>]*>""").Value);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotContain("name=\"DataType\"", html);
        Assert.DoesNotContain("name=\"Stage\"", html);
        Assert.DoesNotContain("Output data type", html);
        Assert.DoesNotContain("<govuk-", html);
    }

    [Fact]
    public async Task Exercise_data_section_renders_scoped_csv_schema_and_add_links()
    {
        var windowId = Guid.NewGuid();
        var exercise = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), Name = "Students", ExerciseType = CheckingExerciseType.PupilData,
            StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(1),
            Datasets = [new() { Id = Guid.NewGuid(), Name = "extra", IngressFile = "ingress/example.csv", SchemaFile = "schema/example.json" }]
        };
        var model = new CreateCheckingExerciseItem { WindowId = windowId, IsEditing = true, DataExercise = exercise };
        var html = await Render("CreateCheckingExercise", model);
        Assert.Contains("Data files", html);
        Assert.Contains("example.csv", html);
        Assert.Contains("example.json", html);
        Assert.Contains($"/admin/windows/{windowId}/PupilData/ingress-file/extra?exerciseId={exercise.Id}", html);
        Assert.Contains("returnToExercise=true", html);
        Assert.Contains($"/admin/windows/{windowId}/PupilData/schema-file/extra?exerciseId={exercise.Id}", html);
        Assert.Contains($"/admin/windows/{windowId}/exercises/{exercise.Id}/data/new", html);
        exercise.Datasets.Clear();
        html = await Render("CreateCheckingExercise", model);
        Assert.Contains("This exercise has no data files", html);
    }

    [Fact]
    public async Task Schema_and_add_data_forms_render_existing_options_and_errors()
    {
        var schemaId = Guid.NewGuid();
        var schemaModel = new SchemaItem
        {
            WindowId = Guid.NewGuid(), ExistingSchemas = [new(schemaId, "Existing pupil schema")],
            ExistingSchemaDatasetId = schemaId, PostUrl = "/schema", CancelUrl = "/exercise"
        };
        var errors = new ModelStateDictionary();
        errors.AddModelError("ExistingSchemaDatasetId", "Choose a schema");
        var html = await Render("Schema", schemaModel, errors);
        Assert.Contains("Existing pupil schema", html);
        Assert.Contains(schemaId.ToString(), html);
        Assert.Contains("Choose a schema", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotContain("name=\"DataType\"", html);
        Assert.DoesNotContain("name=\"Stage\"", html);
        Assert.DoesNotContain("Output data type", html);
        Assert.DoesNotContain("<govuk-", html);

        html = await Render("AddExerciseData", new AddExerciseDataItem { PostUrl = "/add", CancelUrl = "/exercise" });
        Assert.Contains("Add data file", html);
        Assert.Contains("name=\"Name\"", html);
        Assert.Contains("name=\"Required\"", html);
        Assert.DoesNotContain("name=\"DataType\"", html);
        Assert.DoesNotContain("name=\"Stage\"", html);
        Assert.DoesNotContain("Output data type", html);
        Assert.DoesNotContain("<govuk-", html);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Admin_gate_challenges_anonymous_and_hides_ungranted_access(bool authenticated, bool allowed)
    {
        var services = new ServiceCollection();
        var policy = Substitute.For<IAdminAccessPolicy>();
        policy.CanAccessAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(allowed);
        services.AddSingleton(policy);
        using var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity([], authenticated ? "Test" : null))
        };
        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        var gate = (RequireAdminSectionAttribute)Attribute.GetCustomAttribute(
            typeof(CreateCheckingExerciseController), typeof(RequireAdminSectionAttribute))!;
        await gate.OnAuthorizationAsync(context);
        if (!authenticated) Assert.IsType<ChallengeResult>(context.Result);
        else if (!allowed) Assert.IsType<NotFoundResult>(context.Result);
        else Assert.Null(context.Result);
    }

    private static WindowEditItem Model() => new()
    {
        WindowId = Guid.NewGuid(), Title = "Pupil data checking", KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31)
    };

    [Fact]
    public async Task Display_only_tab_renders_data_without_journey_or_closed_message()
    {
        var now = DateTime.Today;
        var exercise = new DfE.CheckPerformanceData.Application.WindowManagement.CheckingDataExercise(
            Guid.NewGuid(), Guid.NewGuid(), "Summary", "Summary", 0,
            null, KeyStages.Post16, true, null, null,
            now.AddDays(-1), now.AddDays(1), now.AddDays(-1), now.AddDays(1), null,
            UsesExerciseStorage: true, DisplayOnly: true);
        IReadOnlyList<DfE.CheckPerformanceData.Web.Controllers.CheckingDataTab> tabs =
            [new(exercise, false, [new Dictionary<string, string> { ["Total"] = "4" }])];
        var html = await Render("/Views/CheckingData/Index.cshtml", tabs);
        Assert.Contains("Summary", html);
        Assert.Contains("Total", html);
        Assert.Contains("You can view and download this data.", html);
        Assert.Contains("Download Summary data", html);
        Assert.DoesNotContain("Request a change", html);
        Assert.DoesNotContain("Checking is closed", html);
    }

    private static async Task<string> Render<T>(string name, T model, ModelStateDictionary? errors = null)
    {
        using var host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddControllersWithViews().AddApplicationPart(typeof(SummaryController).Assembly)
                .AddApplicationPart(typeof(GovUk.Frontend.AspNetCore.TagHelpers.CheckboxesTagHelper).Assembly);
                services.AddGovUkFrontend();
                var content = Substitute.For<DfE.CheckPerformanceData.Application.ContentBlocks.IContentBlockService>();
                content.EnsureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                    .Returns(call => new DfE.CheckPerformanceData.Application.ContentBlocks.ContentBlockDto
                    {
                        Key = call.ArgAt<string>(0), ValueHtml = call.ArgAt<string>(2)
                    });
                services.AddSingleton(content);
                var urls = Substitute.For<IUrlHelper>();
                urls.Action(Arg.Any<UrlActionContext>()).Returns("/admin/windows");
            urls.Content(Arg.Any<string>()).Returns(call => call.Arg<string>().Replace("~/", "/"));
                var urlFactory = Substitute.For<IUrlHelperFactory>();
                urlFactory.GetUrlHelper(Arg.Any<ActionContext>()).Returns(urls);
                services.AddSingleton(urlFactory);
            });
            web.Configure(app => { });
        }).StartAsync();
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var http = new DefaultHttpContext { RequestServices = services, Session = Substitute.For<ISession>() };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var view = services.GetRequiredService<ICompositeViewEngine>()
            .GetView(null, name.StartsWith("/") ? name : $"/Views/WindowAdmin/{name}.cshtml", false);
        Assert.True(view.Success);
        var data = new ViewDataDictionary<T>(services.GetRequiredService<IModelMetadataProvider>(),
            errors ?? new ModelStateDictionary()) { Model = model };
        await using var writer = new StringWriter();
        await view.View.RenderAsync(new ViewContext(action, view.View, data,
            new TempDataDictionary(http, services.GetRequiredService<ITempDataProvider>()), writer, new HtmlHelperOptions()));
        return writer.ToString();
    }
}
