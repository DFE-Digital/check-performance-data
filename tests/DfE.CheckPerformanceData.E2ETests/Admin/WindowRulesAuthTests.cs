using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// #800: the auth matrix for the new per-window rules page (/admin/windows/{id}/rules). It is
// gated by both the manage-window grant (class level) and the rules-config grant (method
// level), so every non-admin principal sees 404 — the same obfuscation as the other admin
// pages, which hide their existence from URL discovery.
[Trait("Category", "FullRegression")]
[Collection("Http")]
public sealed class WindowRulesAuthTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    // The seeded KS4 June window (DevDataSeeder.KeyStage4JuneCheckingWindowId).
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    private string RulesUrl(Guid windowId) => $"{_fixture.BaseUrl}/admin/windows/{windowId}/rules";

    // --- WindowRules_AsAnon_Redirects_To_Signin ---

    [Fact]
    public async Task WindowRules_AsAnon_Redirects_To_Signin()
    {
        try
        {
            await AuthHelpers.ClearImpersonationAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                RulesUrl(SeededWindowId));

            var response = await _fixture.SeedClient.SendAsync(request);

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

    // --- WindowRules_AsNonAdmin_Returns_NotFound ---

    [Fact]
    public async Task WindowRules_AsNonAdmin_Returns_NotFound()
    {
        // A user with no admin section grants gets 404 rather than a redirect to AccessDenied.
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                RulesUrl(SeededWindowId));

            var response = await _fixture.SeedClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- WindowRules_AsEditorOnly_Returns_NotFound ---

    [Fact]
    public async Task WindowRules_AsEditorOnly_Returns_NotFound()
    {
        // The editor holds no manage-window and no rules-config grant, so the page is a 404 —
        // the rules-config gate is admin-only in DefaultAdminAccessSeeder.
        try
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                RulesUrl(SeededWindowId));

            var response = await _fixture.SeedClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- WindowRules_AsAdmin_Returns_200_With_WindowHeading ---

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task WindowRules_AsAdmin_Returns_200_With_WindowHeading()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                RulesUrl(SeededWindowId));

            var response = await _fixture.SeedClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Key Stage 4 June rules", body);
            Assert.Contains("Pupil data start date", body);
            Assert.Contains("Rules version", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- WindowRules_AsAdmin_UnknownWindow_Returns_NotFound ---

    [Fact]
    public async Task WindowRules_AsAdmin_UnknownWindow_Returns_NotFound()
    {
        // Even a fully granted admin gets 404 for a window id the database does not hold.
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                RulesUrl(Guid.NewGuid()));

            var response = await _fixture.SeedClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
