using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class AdminRulesEditTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task EditBranch_AsNonAdmin_Denied()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/rules/outcomes/Inclusion/branches/INC-1/edit");
            var response = await TestHttpClients.SendAsync(request);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? "");
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }

    [Fact]
    public async Task AddBranch_AsAdmin_Returns_Editor()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            // "Inclusion" is a stable seeded outcome. (Don't scrape the first outcomes link —
            // the page also contains the "/admin/rules/outcomes/add" button.)
            using var addReq = new HttpRequestMessage(HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/rules/outcomes/Inclusion/branches/add");
            var addResp = await TestHttpClients.SendAsync(addReq);

            Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);
            var body = await addResp.Content.ReadAsStringAsync();
            Assert.Contains("Add branch", body);
            Assert.Contains("Save branch", body);
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }

    [Fact]
    public async Task Lookups_Edit_Page_AsAdmin_Returns_200()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules/lookups/add");
            var response = await TestHttpClients.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Official languages", await response.Content.ReadAsStringAsync());
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }

    // The async editor (admin-rules.js) needs these hooks to intercept the form, swap the
    // condition tree, update the ETag and show the toast. This guards them against accidental
    // removal — runs everywhere (no browser needed).
    [Fact]
    public async Task EditBranch_AsAdmin_Renders_Async_Editor_Hooks()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/rules/outcomes/Inclusion/branches/INC-REJ/edit");
            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("data-rules-editor-form", body);
            Assert.Contains("id=\"rules-condition-editor\"", body);
            Assert.Contains("id=\"rules-edit-messages\"", body);
            Assert.Contains("id=\"rules-toast-region\"", body);
            Assert.Contains("id=\"LoadETag\"", body);
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }

    // Regression: the outcome detail page renders the recursive _PredicateNode partial.
    // A bare-name partial reference does not resolve from /Views/Admin/Rules for the
    // AdminRules controller, so this page must use a rooted partial path or it 500s.
    [Fact]
    public async Task OutcomeDetail_AsAdmin_Renders_Conditions_Via_Partial()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            // "Inclusion" is a stable seeded outcome with branches that render via the partial.
            using var detailReq = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules/outcomes/Inclusion");
            var detailResp = await TestHttpClients.SendAsync(detailReq);

            Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);
            Assert.Contains("When", await detailResp.Content.ReadAsStringAsync());
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }
}
