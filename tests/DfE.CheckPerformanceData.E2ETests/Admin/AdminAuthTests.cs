using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class AdminAuthTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    // --- Admin_AsAnon_Redirects_To_Signin ---

    [Fact]
    public async Task Admin_AsAnon_Redirects_To_Signin()
    {
        try
        {
            await AuthHelpers.ClearImpersonationAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var location = response.Headers.Location?.ToString() ?? string.Empty;
            Assert.False(string.IsNullOrEmpty(location));
            Assert.DoesNotContain("AccessDenied", location);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- Admin_AsNonAdmin_Redirects_To_AccessDenied ---

    [Fact]
    public async Task Admin_AsNonAdmin_Redirects_To_AccessDenied()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- Admin_AsEditorOnly_Redirects_To_AccessDenied ---

    [Fact]
    public async Task Admin_AsEditorOnly_Redirects_To_AccessDenied()
    {
        // Orthogonality: holding the editor role does NOT implicitly grant admin access.
        try
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- Admin_AsAdmin_Returns_200 ---

    [Fact]
    public async Task Admin_AsAdmin_Returns_200()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Version retention", body);
            Assert.Contains("Visual regression dashboard", body);
            Assert.Contains("Content staging import/export", body);
            Assert.Contains("Coming soon", body);

            // New shape after the grouped admin landing rebuild.
            Assert.Contains("CMS administration", body);
            Assert.Contains("System administration", body);
            Assert.Contains("Deleted pages", body);
            Assert.Contains("Seed sample pages", body);
            Assert.Contains("Queues", body);
            Assert.Contains("Dead letter queue", body);
            Assert.Contains("Rules engine configuration", body);
            Assert.Contains("System settings", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
