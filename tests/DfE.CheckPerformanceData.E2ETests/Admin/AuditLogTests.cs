using System.Net;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// AB#294592 acceptance criteria over HTTP: a real transfer appears in the audit log as Data egress /
// Success with its window, output types and person; the three filters narrow cumulatively; the
// CSV export carries the same filters; a school user gets 404. Audit rows can never be deleted, so
// every assertion is keyed on this walk's own run id.
[Collection("E2E")]
public sealed class AuditLogTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private static readonly Guid WindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");   // the dev seed's KS4 June window

    private HttpClient Client => Fixture.SeedClient;

    private async Task<string> PostFormAsync(string path, IEnumerable<KeyValuePair<string, string>> fields, HttpStatusCode expected = HttpStatusCode.Found)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(Client, "/dev/antiforgery-token");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Fixture.BaseUrl}{path}")
        {
            Content = new FormUrlEncodedContent(fields.Append(new KeyValuePair<string, string>("__RequestVerificationToken", token)))
        };
        request.Headers.Add("Cookie", cookie);
        var response = await TestHttpClients.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        return response.Headers.Location?.ToString() ?? await response.Content.ReadAsStringAsync();
    }

    private async Task<HttpResponseMessage> SendGetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Fixture.BaseUrl}{path}");
        return await TestHttpClients.SendAsync(request);
    }

    private async Task<string> GetAsync(string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await SendGetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task SeedAsync(string outputType, string decision, int count)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{Fixture.BaseUrl}/dev/egress/seed?windowId={WindowId}&outputType={outputType}&decision={decision}&count={count}&laestab=860/4070&urn=142313&reason=pupil-died");
        (await TestHttpClients.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task CleanupAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Fixture.BaseUrl}/dev/egress/cleanup?windowId={WindowId}");
        (await TestHttpClients.SendAsync(request)).EnsureSuccessStatusCode();
    }

    // The <tr> … </tr> carrying this entity id AND action, so assertions cannot be satisfied by another
    // row — a run also has an "Insert" row for its pull.
    private static string RowFor(string page, string entityId, string action)
    {
        var marker = page.IndexOf($"data-entity-id=\"{entityId}\" data-action=\"{action}\"", StringComparison.Ordinal);
        Assert.True(marker >= 0, $"no audit row for {entityId}/{action}");
        var start = page.LastIndexOf("<tr", marker, StringComparison.Ordinal);
        var end = page.IndexOf("</tr>", marker, StringComparison.Ordinal);
        return page[start..end];
    }

    [Fact]
    public async Task A_school_user_gets_404_from_the_audit_log_and_its_export()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(Fixture);
            await GetAsync("/admin/audit-log", HttpStatusCode.NotFound);
            await GetAsync("/admin/audit-log/export", HttpStatusCode.NotFound);
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(Fixture); }
    }

    [Fact]
    public async Task A_transferred_run_is_audited_filterable_and_exported()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            await CleanupAsync();

            // A real run: two approved removals, pulled, preprocessed (no-JS POST), transferred.
            await SeedAsync("RemoveLearners", "auto_approved", 2);
            var resultsUrl = await PostFormAsync("/admin/egress", new Dictionary<string, string> { ["WindowId"] = WindowId.ToString(), ["OutputTypes"] = "RemoveLearners" });
            var runId = Regex.Match(resultsUrl, "runs/([0-9a-f-]{36})").Groups[1].Value;
            var summaryUrl = await PostFormAsync($"/admin/egress/runs/{runId}/preprocessing", []);
            Assert.EndsWith($"/admin/egress/runs/{runId}/summary", summaryUrl);
            var completeUrl = await PostFormAsync($"/admin/egress/runs/{runId}/transfer", []);
            Assert.EndsWith($"/admin/egress/runs/{runId}/complete", completeUrl);

            // Unfiltered: the page renders and the sidebar offers it.
            var unfiltered = await GetAsync("/admin/audit-log");
            Assert.Contains("data-testid=\"audit-log-filters\"", unfiltered);
            Assert.Contains("href=\"/admin/audit-log\"", unfiltered);

            // Egress in this window: the run's row, distinguishable, successful, with its window and files.
            var egress = await GetAsync($"/admin/audit-log?activity=EgressRun&windowId={WindowId}");
            Assert.Contains("data-testid=\"audit-log\"", egress);
            var row = RowFor(egress, runId, "Transfer");
            Assert.Contains(">Data egress</strong>", row);
            Assert.Contains(">Success</strong>", row);
            Assert.Contains("Remove learners", row);
            Assert.Contains("UTC", row);
            Assert.DoesNotContain("Unknown window", row);
            var pulled = RowFor(egress, runId, "Insert");   // the pull, recorded by the generic capture
            Assert.Contains(">Data egress</strong>", pulled);
            Assert.Contains("Run started", pulled);
            Assert.DoesNotContain("govuk-tag--green", pulled);

            // Cumulative: the same window with status Failed drops this run.
            var failed = await GetAsync($"/admin/audit-log?activity=EgressRun&windowId={WindowId}&status=Failed");
            Assert.DoesNotContain(runId, failed);

            // Export reflects the filters: Success carries the run, Failed does not.
            using var export = await SendGetAsync($"/admin/audit-log/export?activity=EgressRun&windowId={WindowId}&status=Success");
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
            var disposition = export.Content.Headers.ContentDisposition;
            Assert.NotNull(disposition);
            Assert.Equal("attachment", disposition!.DispositionType);
            Assert.Contains("audit-log-", disposition.FileName);
            var csv = await export.Content.ReadAsStringAsync();
            Assert.Contains("Timestamp (UTC),User,Activity,Action,Entity type,Entity id,Checking window,Output types,Status", csv);
            var line = csv.Split('\n').Single(l => l.Contains(runId, StringComparison.Ordinal)).TrimEnd('\r');
            Assert.Contains(",Data egress,Transfer,EgressRun,", line);
            Assert.EndsWith(",RemoveLearners,Success", line);

            using var failedExport = await SendGetAsync($"/admin/audit-log/export?activity=EgressRun&windowId={WindowId}&status=Failed");
            Assert.Equal(HttpStatusCode.OK, failedExport.StatusCode);
            Assert.DoesNotContain(runId, await failedExport.Content.ReadAsStringAsync());
        }
        finally
        {
            await CleanupAsync();
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }
}
