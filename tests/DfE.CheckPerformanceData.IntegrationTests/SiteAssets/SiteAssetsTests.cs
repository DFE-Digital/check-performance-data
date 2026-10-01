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
}
