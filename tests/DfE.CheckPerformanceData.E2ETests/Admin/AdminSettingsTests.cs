using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class AdminSettingsTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    // --- Settings_AsAdmin_Returns_200_WithKnownSettings ---

    [Fact]
    public async Task Settings_AsAdmin_Returns_200_WithKnownSettings()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/settings");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("CMS settings", body);
            Assert.Contains("Wiki:PageLength", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- Settings_AsUnprivileged_Redirects_To_AccessDenied ---

    [Fact]
    public async Task Settings_AsUnprivileged_Redirects_To_AccessDenied()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/settings");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- Settings_Save_AsUnprivileged_Redirects_To_AccessDenied ---

    [Fact]
    public async Task Settings_Save_AsUnprivileged_Redirects_To_AccessDenied()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_fixture.BaseUrl}/admin/settings/save");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
