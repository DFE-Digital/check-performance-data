using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// Content staging + content-blocks admin are gated by the content-editor role, which the
// fixture impersonates for the whole collection. HTTP-level checks keep these robust against
// CMS content state (the export endpoint works even on an empty environment).
[Collection("E2E")]
public sealed class ContentStagingTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task ContentStaging_AsEditor_Index_Returns200_WithExportAndImportControls()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{_fixture.BaseUrl}/admin/content-staging");

        var response = await TestHttpClients.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Content staging import/export", body);
        Assert.Contains("Export everything", body);
        Assert.Contains("Preview import", body);
    }

    [Fact]
    public async Task ContentStaging_SelectiveExportPage_Returns200()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{_fixture.BaseUrl}/admin/content-staging/select");

        var response = await TestHttpClients.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ContentStaging_ExportEverything_ReturnsSchemaVersionedJsonBundle()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{_fixture.BaseUrl}/admin/content-staging/export");

        var response = await TestHttpClients.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("cpd-content-v2", body); // the bundle's schema marker
    }

    [Fact]
    public async Task ContentBlocksAdmin_AsEditor_Returns200()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{_fixture.BaseUrl}/admin/content-blocks");

        var response = await TestHttpClients.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ContentStaging_AsAnon_Redirects()
    {
        try
        {
            await AuthHelpers.ClearImpersonationAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{_fixture.BaseUrl}/admin/content-staging");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
