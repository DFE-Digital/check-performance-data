using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class AdminRulesM4Tests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task AddOutcome_AsNonAdmin_Denied()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules/outcomes/add");
            var response = await TestHttpClients.SendAsync(request);
            // Non-admin users get 404 rather than 302 to AccessDenied — the admin surface
            // is obfuscated from users with no section grant.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }

    [Fact]
    public async Task AddOutcome_Page_AsAdmin_Returns_200()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules/outcomes/add");
            var response = await TestHttpClients.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Add outcome", await response.Content.ReadAsStringAsync());
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }

    [Fact]
    public async Task Delete_Confirm_For_Form_Bound_Outcome_Is_NotFound()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules/outcomes/Inclusion/delete");
            var response = await TestHttpClients.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }

    [Fact]
    public async Task History_Page_AsAdmin_Returns_200()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules/history/Rules");
            var response = await TestHttpClients.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }

    // The seed writes blobs directly (bypassing the version table), so no version row is
    // guaranteed here. Verify the rollback route is wired and handles an unknown version id
    // gracefully (NotFound, not a 500 or a missing-route error). The button's render on a real
    // version page is covered by the unit VM test + manual smoke.
    [Fact]
    public async Task Rollback_Confirm_For_Unknown_Version_Is_NotFound()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/rules/history/Rules/2147483647/rollback");
            var response = await TestHttpClients.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }
}
