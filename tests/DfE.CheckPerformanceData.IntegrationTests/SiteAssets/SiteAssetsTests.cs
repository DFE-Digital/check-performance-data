using System.Net;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Application.SiteAssets;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.SiteAssets;

[Collection(nameof(PostgresCollection))]
public sealed class SiteAssetsTests(PostgresFixture fixture)
{
    private async Task TruncateSettingsAsync()
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""Settings"";";
        await cmd.ExecuteNonQueryAsync();
    }

    // A fresh service, context and cache each time, so a read sees committed database state.
    private SiteAssetService NewService()
    {
        var context = fixture.CreateContext();
        return new SiteAssetService(
            new SettingService(new SettingRepository(context)),
            new MemoryCache(new MemoryCacheOptions()));
    }

    [Fact]
    public async Task Save_ThenRead_RoundTripsThroughTheDatabase()
    {
        await TruncateSettingsAsync();

        var result = await NewService().SaveAsync(new SiteAssetContent("h1{outline:2px solid red}", "console.log('hi')", true, false));
        var read = await NewService().GetAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("h1{outline:2px solid red}", read.Css);
        Assert.Equal("console.log('hi')", read.Js);
        Assert.True(read.CssEnabled);
        Assert.False(read.JsEnabled);
        Assert.True(read.ServesCss);
        Assert.False(read.ServesJs);
    }

    [Fact]
    public async Task Save_StampsTheVersionAsAUtcTimestamp_ThatSurvivesTheDatabase()
    {
        await TruncateSettingsAsync();
        var before = DateTime.UtcNow.AddSeconds(-1);

        await NewService().SaveAsync(new SiteAssetContent("h1{}", "", true, true));
        var read = await NewService().GetAsync();

        Assert.Matches("^[0-9]{14}$", read.CssVersion);
        var stamped = DateTime.ParseExact(read.CssVersion, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.InRange(stamped, before, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal("", read.JsVersion);
    }

    // The content, switches and versions are written together. If the store fails part-way, nothing
    // is kept: new content must never be served under the version of the old content, which browsers
    // may already hold for a year.
    [Fact]
    public async Task Save_WhenALaterWriteFails_NothingIsWritten()
    {
        await TruncateSettingsAsync();
        await NewService().SaveAsync(new SiteAssetContent("h1{color:red}", "", true, true));
        var before = await NewService().GetAsync();

        var failing = new FailingSettingRepository(new SettingRepository(fixture.CreateContext()), SettingKeys.SiteCssSavedAt);
        var result = await new SiteAssetService(new SettingService(failing), new MemoryCache(new MemoryCacheOptions()),
            new FixedClock(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero)))
            .SaveAsync(new SiteAssetContent("h1{color:blue}", "", true, true));
        var after = await NewService().GetAsync();

        Assert.False(result.Succeeded);
        Assert.True(failing.Failed);
        Assert.Equal("h1{color:red}", after.Css);
        Assert.Equal(before.CssVersion, after.CssVersion);
    }

    // A failed save leaves nothing that makes the retry look like "no change", so the retry stamps a
    // new version. The retry reuses the same context, as a resubmitted form might.
    [Fact]
    public async Task Save_RetriedAfterAFailure_WritesTheContentAndANewVersion()
    {
        await TruncateSettingsAsync();
        await NewService().SaveAsync(new SiteAssetContent("h1{color:red}", "", true, true));
        var before = await NewService().GetAsync();

        var failing = new FailingSettingRepository(new SettingRepository(fixture.CreateContext()), SettingKeys.SiteCssSavedAt);
        var service = new SiteAssetService(new SettingService(failing), new MemoryCache(new MemoryCacheOptions()),
            new FixedClock(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var first = await service.SaveAsync(new SiteAssetContent("h1{color:blue}", "", true, true));
        var retry = await service.SaveAsync(new SiteAssetContent("h1{color:blue}", "", true, true));
        var after = await NewService().GetAsync();

        Assert.False(first.Succeeded);
        Assert.True(retry.Succeeded);
        Assert.Equal("h1{color:blue}", after.Css);
        Assert.Equal("20990101000000", after.CssVersion);
        Assert.NotEqual(before.CssVersion, after.CssVersion);
    }

    [Fact]
    public async Task Save_UnchangedContentWithATrailingNewline_KeepsTheVersion_ButARealEditMovesIt()
    {
        await TruncateSettingsAsync();
        SiteAssetService At(int year) => new(new SettingService(new SettingRepository(fixture.CreateContext())),
            new MemoryCache(new MemoryCacheOptions()), new FixedClock(new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        await At(2030).SaveAsync(new SiteAssetContent("body{}", "", true, true));
        await At(2031).SaveAsync(new SiteAssetContent("body{}\n", "", true, true));
        var unchanged = await NewService().GetAsync();
        await At(2032).SaveAsync(new SiteAssetContent("body{margin:0}\n", "", true, true));
        var edited = await NewService().GetAsync();

        Assert.Equal("20300101000000", unchanged.CssVersion);
        Assert.Equal("body{margin:0}", edited.Css);
        Assert.Equal("20320101000000", edited.CssVersion);
    }

    [Fact]
    public async Task Save_LargeContent_IsStoredWhole()
    {
        await TruncateSettingsAsync();
        var big = string.Concat(Enumerable.Repeat("a{color:red}\n", SiteAssetService.MaxLength / 13));

        await NewService().SaveAsync(new SiteAssetContent(big, "", true, true));

        Assert.Equal(big.Trim(), (await NewService().GetAsync()).Css);
    }

    [Fact]
    public async Task Save_IsWrittenToTheAuditTrail()
    {
        await TruncateSettingsAsync();
        var marker = $"/* audit-{Guid.NewGuid():N} */ h1{{color:red}}";

        await NewService().SaveAsync(new SiteAssetContent(marker, "", true, true));

        await using var verify = fixture.CreateContext();
        var entries = await verify.AuditEntries
            .Where(a => a.EntityId == SettingKeys.SiteCss)
            .ToListAsync();

        Assert.Contains(entries, a => a.NewValues != null && a.NewValues.Contains("audit-"));
    }

    [Fact]
    public async Task Endpoints_ServeStoredAssets_AnonymouslyWithTheRightContentTypes()
    {
        await TruncateSettingsAsync();
        await NewService().SaveAsync(new SiteAssetContent("h1{outline:2px solid red}", "window.siteJs=1;", true, true));

        using var host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddDbContext<PortalDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
                services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
                services.AddScoped<ISettingRepository, SettingRepository>();
                services.AddScoped<ISettingService, SettingService>();
                services.AddScoped<DfE.CheckPerformanceData.Application.CurrentUser.ICurrentUserService, FakeCurrentUserService>();
                services.AddMemoryCache();
                services.AddScoped<ISiteAssetService, SiteAssetService>();
                services.AddControllers().AddApplicationPart(typeof(SiteAssetsController).Assembly);
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapControllers());
            });
        }).StartAsync();
        var client = host.GetTestClient();

        using var css = await client.GetAsync("/cms/site.css");
        using var js = await client.GetAsync("/cms/site.js");

        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal("text/css", css.Content.Headers.ContentType!.MediaType);
        Assert.Equal("h1{outline:2px solid red}", await css.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Equal("text/javascript", js.Content.Headers.ContentType!.MediaType);
        Assert.Equal("window.siteJs=1;", await js.Content.ReadAsStringAsync());
    }

    // Passes everything to the real repository, except that the first write of one key fails, as a
    // dropped connection or a timeout would part-way through a save.
    private sealed class FailingSettingRepository(ISettingRepository inner, string failOnKey) : ISettingRepository
    {
        public bool Failed { get; private set; }

        public Task<Dictionary<string, string>> GetAllAsync() => inner.GetAllAsync();
        public Task<string?> GetValueAsync(string key) => inner.GetValueAsync(key);
        public Task ExecuteInTransactionAsync(Func<Task> work) => inner.ExecuteInTransactionAsync(work);

        public Task UpsertAsync(string key, string value)
        {
            FailOnce(key);
            return inner.UpsertAsync(key, value);
        }

        public Task DeleteAsync(string key)
        {
            FailOnce(key);
            return inner.DeleteAsync(key);
        }

        private void FailOnce(string key)
        {
            if (key != failOnKey || Failed) return;
            Failed = true;
            throw new InvalidOperationException("Simulated store failure.");
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
