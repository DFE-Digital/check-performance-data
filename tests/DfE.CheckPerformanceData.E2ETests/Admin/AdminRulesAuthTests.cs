using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class AdminRulesAuthTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    // --- Rules_AsAnon_Redirects_To_Signin ---

    [Fact]
    public async Task Rules_AsAnon_Redirects_To_Signin()
    {
        try
        {
            await AuthHelpers.ClearImpersonationAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/rules");

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

    // --- Rules_AsNonAdmin_Returns_NotFound ---

    [Fact]
    public async Task Rules_AsNonAdmin_Returns_NotFound()
    {
        // Users with no section grants get 404 rather than 302 to AccessDenied — admin
        // surface is deliberately obfuscated from URL discovery.
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/rules");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- Rules_AsEditorOnly_Returns_NotFound ---

    [Fact]
    public async Task Rules_AsEditorOnly_Returns_NotFound()
    {
        // The rules-config section is admin-only in DefaultAdminAccessSeeder, so an editor
        // without the extra grant sees 404 — same obfuscation as any other ungranted role.
        try
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/rules");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- Rules_AsAdmin_Returns_200_With_Both_Cards ---

    [Fact]
    public async Task Rules_AsAdmin_Returns_200_With_Both_Cards()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/rules");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Rules engine configuration", body);
            Assert.Contains("Decision rules", body);
            Assert.Contains("Country languages", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
