using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

[Collection("E2E")]
public sealed class QueueAdminTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    // --- Non-admin gets 404 (obfuscated) from the queue admin surface ---

    [Fact]
    public async Task QueueAdmin_AsNonAdmin_Returns_NotFound()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/queues");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- Admin sees the System administration tile + the queue/DLQ views render (D-06) ---

    [Fact]
    public async Task QueueAdmin_AsAdmin_RendersTileQueuesAndDlqView()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var adminRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin");
            var adminResponse = await TestHttpClients.SendAsync(adminRequest);
            Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
            var adminBody = await adminResponse.Content.ReadAsStringAsync();
            Assert.Contains("System administration", adminBody);
            Assert.Contains("/admin/queues", adminBody);

            using var queuesRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/queues");
            var queuesResponse = await TestHttpClients.SendAsync(queuesRequest);
            Assert.Equal(HttpStatusCode.OK, queuesResponse.StatusCode);

            using var dlqRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/queues/dlq");
            var dlqResponse = await TestHttpClients.SendAsync(dlqRequest);
            Assert.Equal(HttpStatusCode.OK, dlqResponse.StatusCode);

            var dlqBody = await dlqResponse.Content.ReadAsStringAsync();
            Assert.Contains("Dead-letter", dlqBody);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- Top-bar Messages badge renders and links to the Messages group landing ---

    [Fact]
    public async Task AdminChrome_RendersMessagesBadge_LinkingToMessagesLanding()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/queues");
            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // The admin chrome carries a single consolidated Messages badge linking to the
            // group landing; the individual DLQ inbox is still reachable from the sidebar.
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("href=\"/admin/Messages\"", body);
            Assert.DoesNotContain("href=\"/admin/queues/dlq\"><strong", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- An admin redrive flow completes: confirm form submits, redirects, message gone ---

    [Fact]
    public async Task QueueAdmin_AsAdmin_RedriveFlow_RemovesMessageFromDlq()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            var id = await SeedHelpers.SeedDeadLetterAsync(_fixture.SeedClient);
            Assert.True(await DlqRowVisibleAsync(id), "Seeded dead letter should be visible before redrive.");

            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/admin/queues/dlq");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_fixture.BaseUrl}/admin/queues/dlq/{id}/redrive")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("__RequestVerificationToken", token)
                })
            };
            request.Headers.Add("Cookie", $"{TestHttpClients.ImpersonationCookieHeader}; {cookie}");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/admin/queues/dlq", response.Headers.Location?.ToString() ?? string.Empty);

            // The redriven message is no longer dead-lettered.
            Assert.False(await DlqRowVisibleAsync(id), "Redriven message should no longer appear in the DLQ.");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- An admin purge flow completes: confirm form submits, redirects, message gone ---

    [Fact]
    public async Task QueueAdmin_AsAdmin_PurgeFlow_RemovesMessageFromDlq()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            var id = await SeedHelpers.SeedDeadLetterAsync(_fixture.SeedClient);
            Assert.True(await DlqRowVisibleAsync(id), "Seeded dead letter should be visible before purge.");

            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/admin/queues/dlq");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_fixture.BaseUrl}/admin/queues/dlq/{id}/purge")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("__RequestVerificationToken", token)
                })
            };
            request.Headers.Add("Cookie", $"{TestHttpClients.ImpersonationCookieHeader}; {cookie}");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/admin/queues/dlq", response.Headers.Location?.ToString() ?? string.Empty);

            Assert.False(await DlqRowVisibleAsync(id), "Purged message should no longer appear in the DLQ.");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- A purge POST without an antiforgery token is rejected (CSRF guard) ---
    //
    // The antiforgery failure surfaces as a 404 rather than the ASP.NET default 400 because
    // `UseStatusCodePagesWithReExecute("/Home/NotFound")` re-executes empty-body 4xx/5xx
    // through the NotFound page — matching the RequireAdminSection obfuscation pattern
    // documented in the sibling admin-403 tests. The invariant this test pins is only
    // "the CSRF guard fires"; whether the outward code is 400 or 404 is a middleware
    // decision, not a security contract.
    [Fact]
    public async Task QueueAdmin_Purge_MissingAntiforgery_IsRejected()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            var id = await SeedHelpers.SeedDeadLetterAsync(_fixture.SeedClient);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_fixture.BaseUrl}/admin/queues/dlq/{id}/purge");

            var response = await TestHttpClients.SendAsync(request);

            Assert.True(
                response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
                $"Expected the CSRF guard to reject the purge (400 or 404); got {(int)response.StatusCode} {response.StatusCode}.");
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    private async Task<bool> DlqRowVisibleAsync(Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/queues/dlq");
        var response = await TestHttpClients.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        return body.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
