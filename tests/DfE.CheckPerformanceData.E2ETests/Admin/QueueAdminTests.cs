using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

[Collection("E2E")]
[Trait("Category", "W0")]
public sealed class QueueAdminTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    // --- Non-admin is blocked from the queue admin surface ---

    [Fact]
    public async Task QueueAdmin_AsNonAdmin_Redirects_To_AccessDenied()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/queues");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- Admin sees the queue admin landing + DLQ view renders (D-06) ---

    [Fact]
    public async Task QueueAdmin_AsAdmin_RendersQueuesAndDlqView()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

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

    // --- Top-bar DLQ badge renders and links to the DLQ view (D-06) ---

    [Fact]
    public async Task AdminChrome_RendersDlqBadge_LinkingToDlqView()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/queues");
            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // The admin chrome carries a DLQ-count badge linking to the DLQ view.
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("/admin/queues/dlq", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
