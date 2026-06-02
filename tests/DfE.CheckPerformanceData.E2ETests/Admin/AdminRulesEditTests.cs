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

            using var listReq = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules/outcomes");
            var listBody = await (await TestHttpClients.SendAsync(listReq)).Content.ReadAsStringAsync();
            Assert.Contains("/admin/rules/outcomes/", listBody);

            var start = listBody.IndexOf("/admin/rules/outcomes/", StringComparison.Ordinal);
            var href = listBody.Substring(start, listBody.IndexOf('"', start) - start);
            var key = href.Replace("/admin/rules/outcomes/", "").Trim('/');

            using var addReq = new HttpRequestMessage(HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/rules/outcomes/{key}/branches/add");
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

    // Regression: the outcome detail page renders the recursive _PredicateNode partial.
    // A bare-name partial reference does not resolve from /Views/Admin/Rules for the
    // AdminRules controller, so this page must use a rooted partial path or it 500s.
    [Fact]
    public async Task OutcomeDetail_AsAdmin_Renders_Conditions_Via_Partial()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var listReq = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/rules/outcomes");
            var listBody = await (await TestHttpClients.SendAsync(listReq)).Content.ReadAsStringAsync();
            var start = listBody.IndexOf("/admin/rules/outcomes/", StringComparison.Ordinal);
            var href = listBody.Substring(start, listBody.IndexOf('"', start) - start);

            using var detailReq = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}{href}");
            var detailResp = await TestHttpClients.SendAsync(detailReq);

            Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);
            Assert.Contains("When", await detailResp.Content.ReadAsStringAsync());
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(_fixture); }
    }
}
