using System.Text;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class ImpersonationJourneyPreviewStorageTests(PlaywrightFixture fixture)
    : SeedingPageTest(fixture)
{
    private static readonly Guid WindowId = Guid.Parse("7D2E9A14-5C38-4B6F-A0E1-3F8B6C2D4A97");

    [SkippableFact]
    [Trait("Category", "Smoke")]
    public async Task Impersonated_journey_draft_upload_and_submit_leave_request_storage_unchanged()
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        await AttachCookieToContextAsync(adminCookie);

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/impersonation");
        await Page.Locator("#Laestab").FillAsync("8604070");
        await Page.Locator("#Urn").FillAsync("142313");
        await Page.Locator("#LowestAge").FillAsync("4");
        await Page.Locator("#HighestAge").FillAsync("11");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Start read-only impersonation" }).ClickAsync();
        await Page.WaitForURLAsync("**/admin/impersonation/choices");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Go establishment home page" }).ClickAsync();

        var requestsBefore = await ChangeRequestProbeHelper.TryCountChangeRequestsAsync();
        Skip.If(requestsBefore is null, ChangeRequestProbeHelper.UnavailableReason);
        var blobsBefore = await EvidenceBlobProbeHelper.TryCountEvidenceAsync(WindowId);
        Skip.If(blobsBefore is null, "Cannot reach Azurite for the evidence blob probe.");
        var answerCanary = $"preview-canary-{Guid.NewGuid():N}";
        var canaryInSessionBefore = await ChangeRequestProbeHelper.TryContainsSessionValueAsync(answerCanary);
        Skip.If(canaryInSessionBefore is null, ChangeRequestProbeHelper.UnavailableReason);
        Assert.False(canaryInSessionBefore);

        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{WindowId}");
        await Page.GetByLabel("Remove").First.CheckAsync();
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/pupil-search/**");
        await Page.Locator("#pupil-search").FillAsync("Smith");
        await Page.Locator("li[role='option']").GetByText("Smith, Alice").ClickAsync();
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/reason");
        await Page.Locator("input[name='q_reason'][value='not-on-june-list']").CheckAsync();
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/evidence");

        await Page.Locator("input[name='fileUpload']").SetInputFilesAsync(new FilePayload
        {
            Name = "preview-evidence.pdf",
            MimeType = "application/pdf",
            Buffer = OnePagePdf()
        });
        await Page.Locator("#q_how_evidence_supports").FillAsync(answerCanary);
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/summary");
        await Expect(Page.GetByText("preview changes", new() { Exact = false })).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save and continue later" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/summary");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Preview only" })).ToBeVisibleAsync();
        await AxeChecks.ExpectNoViolationsAsync(Page, "Impersonation journey preview");

        await ChangeRequestProbeHelper.AssertNoRowsCreatedBetweenAsync(requestsBefore);
        Assert.Equal(blobsBefore, await EvidenceBlobProbeHelper.TryCountEvidenceAsync(WindowId));
        Assert.False(await ChangeRequestProbeHelper.TryContainsSessionValueAsync(answerCanary));
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

    private async Task AttachCookieToContextAsync(string? cookieHeader)
    {
        if (string.IsNullOrEmpty(cookieHeader)) return;
        var equalsIndex = cookieHeader.IndexOf('=');
        if (equalsIndex <= 0) return;
        await Context.AddCookiesAsync([new Cookie
        {
            Name = cookieHeader[..equalsIndex],
            Value = cookieHeader[(equalsIndex + 1)..],
            Url = Fixture.BaseUrl
        }]);
    }

    private static byte[] OnePagePdf()
    {
        const string content = "%PDF-1.4\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n"
                              + "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n"
                              + "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >> endobj\n"
                              + "trailer << /Root 1 0 R >>\n%%EOF";
        return Encoding.ASCII.GetBytes(content);
    }
}
