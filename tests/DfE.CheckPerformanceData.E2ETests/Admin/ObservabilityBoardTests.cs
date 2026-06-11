using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// The observability dashboard renders the workflow board server-side: a left-to-right pipeline
// skeleton with the five labelled stage nodes and an accessible textual parallel (counts per
// stage + recent transitions) so the information is available without motion. The export CTA and
// the board/export scripts are wired into the page. All assertions are DOM-level, not pixel.
[Collection("E2E")]
[Trait("Category", "W0")]
public sealed class ObservabilityBoardTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    private const string DashboardPath = "/admin/observability";

    private static readonly string[] StageLabels =
    {
        "Submit",
        "Rules-queue",
        "Rules engine",
        "Zendesk-queue",
        "Ticket",
    };

    // --- A non-admin cannot reach the dashboard ---

    [Fact]
    public async Task Dashboard_AsNonAdmin_Redirects_To_AccessDenied()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}{DashboardPath}");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- The board skeleton renders the five labelled stage nodes ---

    [Fact]
    public async Task Dashboard_RendersBoardSkeleton_WithFiveStageNodes()
    {
        var body = await LoadDashboardAsAdminAsync();

        Assert.Contains("obs-board", body);

        foreach (var label in StageLabels)
        {
            Assert.Contains(label, body);
        }
    }

    // --- The board ships an accessible textual parallel (counts per stage + recent transitions) ---

    [Fact]
    public async Task Dashboard_RendersAccessibleTextualParallel()
    {
        var body = await LoadDashboardAsAdminAsync();

        // The accessible parallel region carries per-stage counts and the recent transitions list,
        // so a non-visual user gets the same information the animation conveys.
        Assert.Contains("obs-board__parallel", body);
        Assert.Contains("Pipeline state", body);
        Assert.Contains("Recent transitions", body);
    }

    // --- The export CTA and the board/export scripts are present and wired ---

    [Fact]
    public async Task Dashboard_RendersExportCta_AndWiresBoardAndExportScripts()
    {
        var body = await LoadDashboardAsAdminAsync();

        Assert.Contains("Export this view", body);
        Assert.Contains("observability-board.js", body);
        Assert.Contains("observability-export.js", body);
    }

    private async Task<string> LoadDashboardAsAdminAsync()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}{DashboardPath}");
            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
