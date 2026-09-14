using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// AB#294553 acceptance criteria, walked over HTTP (so they run on every platform) with one
// browser fact for the streamed progress. The seeded KS4 June window is the dev seed's; every run
// is cleaned up first because a transferred run blocks the pair forever by design.
[Collection("E2E")]
public sealed class DataEgressTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private static readonly Guid WindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    private HttpClient Client => Fixture.SeedClient;   // the fixture's BaseAddress-bearing client used by SeedHelpers

    // IEnumerable<KeyValuePair>, not Dictionary: ASP.NET Core model-binds a List<T> from
    // genuinely repeated identical keys (OutputTypes=A&OutputTypes=B), not from a plain key
    // mixed with an indexed one (OutputTypes=A&OutputTypes[1]=B) — verified against the running
    // app; the latter silently binds only the first value. A Dictionary<string,string> cannot
    // hold the "OutputTypes" key twice, so callers needing more than one value build a list.
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

    private async Task<string> GetAsync(string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path.StartsWith("http") ? path : $"{Fixture.BaseUrl}{path}");
        var response = await TestHttpClients.SendAsync(request);
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

    // S5: the Task 12 duplicate-error-summary fix (moving the output-types error out of the
    // fieldset entirely) detached the field error from its fieldset. Confirms live that exactly
    // one error summary still renders and the fieldset's aria-describedby now includes the error.
    [Fact]
    public async Task Pull_with_nothing_selected_shows_one_error_summary_and_associates_the_checkbox_error()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);

            var html = await PostFormAsync("/admin/egress", [], HttpStatusCode.OK);

            Assert.Equal(1, Regex.Matches(html, "There is a problem").Count);
            Assert.Contains("govuk-form-group govuk-form-group--error", html);
            Assert.Matches("<fieldset[^>]*aria-describedby=\"OutputTypes-hint OutputTypes-error\"", html);
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(Fixture); }
    }

    [Fact]
    public async Task A_school_user_gets_404_from_the_egress_section()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(Fixture);
            await GetAsync("/admin/egress", HttpStatusCode.NotFound);
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(Fixture); }
    }

    [Fact]
    public async Task The_whole_journey_over_plain_http_pull_preprocess_summary_transfer_complete_then_refused()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            await CleanupAsync();
            await SeedAsync("RemoveLearners", "auto_approved", 2);
            await SeedAsync("RemoveLearners", "rejected", 1);
            await SeedAsync("RemoveLearners", "none", 1);
            await SeedAsync("NewLearners", "approved", 1);

            // Pull → Results
            var resultsUrl = await PostFormAsync("/admin/egress",
            [
                new("WindowId", WindowId.ToString()),
                new("OutputTypes", "NewLearners"),
                new("OutputTypes", "RemoveLearners")
            ]);
            Assert.Matches("/admin/egress/runs/[0-9a-f-]{36}/results$", resultsUrl);
            var runId = Regex.Match(resultsUrl, "runs/([0-9a-f-]{36})").Groups[1].Value;
            var results = await GetAsync(resultsUrl);
            Assert.Contains("data-testid=\"egress-results-removelearners\"", results);
            Assert.Contains("data-testid=\"egress-results-newlearners\"", results);
            Assert.Contains("Auto approved", results);
            Assert.Contains("Rejected", results);
            Assert.Contains("No Zendesk ticket", results);
            Assert.Equal(5, Regex.Matches(results, "DEV-EGRESS-").Count);

            // Preprocessing (no-JS POST) → Summary
            var summaryUrl = await PostFormAsync($"/admin/egress/runs/{runId}/preprocessing", []);
            Assert.EndsWith($"/admin/egress/runs/{runId}/summary", summaryUrl);
            var summary = await GetAsync(summaryUrl);
            Assert.Contains("cypmd/extracts_input", summary);
            Assert.Matches("CYPMD_LDS_KS4_RemoveLearners_\\d{4}_\\d{2}_\\d{2}\\.csv", summary);
            Assert.Matches("CYPMD_LDS_KS4_NewLearners_\\d{4}_\\d{2}_\\d{2}\\.csv", summary);

            // Download: only the two approved removals, in spec shape
            var csv = await GetAsync($"/admin/egress/runs/{runId}/download/RemoveLearners");
            var lines = csv.Split("\r\n");
            Assert.Equal("Correction_ID,Correction_Type,Correction_Reason,Key_Stage,Establishment_Number,Surname,Forename,Sex,Date_of_Birth,Cycle_Year,Cycle_Month,Local_Authority,Learner_ID", lines[0]);
            Assert.Equal(3, lines.Length);
            Assert.Contains(",31,4,KS4,4070,", lines[1]);
            Assert.Contains(",860,", lines[1]);
            Assert.DoesNotContain("\n", lines[2]);

            // Transfer → Complete
            var completeUrl = await PostFormAsync($"/admin/egress/runs/{runId}/transfer", []);
            Assert.EndsWith($"/admin/egress/runs/{runId}/complete", completeUrl);
            var complete = await GetAsync(completeUrl);
            Assert.Contains("Transfer complete", complete);
            Assert.Contains("Files transferred", complete);

            // Refused: the pair has been transferred
            var refused = await PostFormAsync("/admin/egress", new Dictionary<string, string> { ["WindowId"] = WindowId.ToString(), ["OutputTypes"] = "RemoveLearners" }, HttpStatusCode.OK);
            Assert.Contains("already been transferred", refused);

            // Listed as completed
            var index = await GetAsync("/admin/egress");
            Assert.Contains("data-testid=\"egress-completed-runs\"", index);
        }
        finally
        {
            await CleanupAsync();
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    [Fact]
    public async Task A_failing_record_fails_the_batch_and_the_failure_page_names_it()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            await CleanupAsync();
            await SeedAsync("RemoveLearners", "approved", 1);
            using (var bad = new HttpRequestMessage(HttpMethod.Post,
                       $"{Fixture.BaseUrl}/dev/egress/seed?windowId={WindowId}&outputType=RemoveLearners&decision=approved&count=1&laestab=860/4070&urn=142313&reason=other"))
                (await TestHttpClients.SendAsync(bad)).EnsureSuccessStatusCode();

            var resultsUrl = await PostFormAsync("/admin/egress", new Dictionary<string, string> { ["WindowId"] = WindowId.ToString(), ["OutputTypes"] = "RemoveLearners" });
            var runId = Regex.Match(resultsUrl, "runs/([0-9a-f-]{36})").Groups[1].Value;
            var failedUrl = await PostFormAsync($"/admin/egress/runs/{runId}/preprocessing", []);
            Assert.EndsWith($"/admin/egress/runs/{runId}/failed", failedUrl);
            var failed = await GetAsync(failedUrl);
            Assert.Contains("No records were transferred", failed);
            Assert.Contains("Correction_Reason", failed);
            Assert.Contains("other", failed);

            // The pair is free again: a new run can start.
            await PostFormAsync("/admin/egress", new Dictionary<string, string> { ["WindowId"] = WindowId.ToString(), ["OutputTypes"] = "RemoveLearners" });
        }
        finally
        {
            await CleanupAsync();
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    [SkippableFact]
    public async Task With_javascript_the_steps_tick_as_the_stream_reports_them()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright browser test Linux-only");
        try
        {
            AttachCookieToContext(await AuthHelpers.ImpersonateAsAdminAsync(Fixture));
            await CleanupAsync();
            await SeedAsync("RemoveLearners", "auto_approved", 1);

            await Page.GotoAsync($"{Fixture.BaseUrl}/admin/egress");
            await Page.SelectOptionAsync("select#WindowId", WindowId.ToString());
            await Page.CheckAsync("input[name='OutputTypes'][value='RemoveLearners']");
            await Page.ClickAsync("[data-testid='egress-pull']");
            await Page.ClickAsync("[data-testid='egress-proceed']");
            await Expect(Page.Locator("h1")).ToContainTextAsync("Preprocessing data");

            await Page.ClickAsync("[data-testid='egress-run-preprocessing']");

            // A single record against the local database preprocesses in well under a second, so
            // the browser can navigate to Summary before ever rendering an intermediate step's
            // "Done" state — asserting on step 1 (or even step 8) is a race the pipeline usually
            // wins. The only assertion robust to that speed is the terminal outcome the stream
            // drives the page to.
            await Expect(Page.Locator("h1")).ToContainTextAsync("Confirm transfer to LDS", new() { Timeout = 15_000 });
        }
        finally
        {
            await CleanupAsync();
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    private void AttachCookieToContext(string? cookieHeader)
    {
        if (string.IsNullOrEmpty(cookieHeader)) return;
        var equalsIndex = cookieHeader.IndexOf('=');
        if (equalsIndex <= 0) return;

        Context.AddCookiesAsync([new Microsoft.Playwright.Cookie
        {
            Name = cookieHeader[..equalsIndex],
            Value = cookieHeader[(equalsIndex + 1)..],
            Url = Fixture.BaseUrl
        }]).GetAwaiter().GetResult();
    }
}
