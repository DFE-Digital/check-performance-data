using System.Text;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#304900 — "The evidence upload accepted 7 PDF files. The limit is meant to be 6 files."
//   AC1: the upload rejects a 7th file when 6 have already been added;
//   AC2: the user is told why the file was rejected.
// Walked on the KS4 Remove journey's evidence page; every journey's upload is the same partial
// and the same controller method.
[Collection("E2E")]
public sealed class EvidenceUploadFileLimitTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // Seeded KS4June window whose pupil blob data SeedPupilData uploads (same id as
    // Ks4JourneyTests and PermanentExclusionCopyTests).
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    // Kingsmead School included pupil (Pincl 401), so the permanent-exclusion reason is offered.
    private const string PupilSurname = "Smith";
    private const string PupilFirstName = "Alice";

    private const string FileLimitMessage =
        "You can only upload 6 files. Remove a file before you upload another.";

    private ILocator UploadedFileLinks =>
        Page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex(@"^evidence-\d\.pdf$") });

    [RetryFact(3)]
    public async Task ASeventhFileIsRejectedWithTheReason_AndAcceptedOnceAFileIsRemoved()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToEvidencePageAsync();

        for (var i = 1; i <= 6; i++)
        {
            await UploadAsync($"evidence-{i}.pdf");
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = $"evidence-{i}.pdf", Exact = true })).ToBeVisibleAsync();
        }
        await Expect(UploadedFileLinks).ToHaveCountAsync(6);
        await Expect(Page.GetByText("6 of 6 files added.")).ToBeVisibleAsync();

        // AC1 + AC2: the seventh is refused, and the page says why — in the error summary and
        // beside the file input.
        await UploadAsync("evidence-7.pdf");
        await Expect(Page.Locator(".govuk-error-summary")).ToContainTextAsync(FileLimitMessage);
        await Expect(Page.Locator("#fileUpload-error")).ToContainTextAsync(FileLimitMessage);
        await Expect(UploadedFileLinks).ToHaveCountAsync(6);
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "evidence-7.pdf", Exact = true })).ToHaveCountAsync(0);

        // The message tells the school what to do; doing it works.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove evidence-1.pdf", Exact = true }).ClickAsync();
        await Expect(UploadedFileLinks).ToHaveCountAsync(5);
        await UploadAsync("evidence-7.pdf");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "evidence-7.pdf", Exact = true })).ToBeVisibleAsync();
        await Expect(UploadedFileLinks).ToHaveCountAsync(6);
        await Expect(Page.Locator(".govuk-error-summary")).ToHaveCountAsync(0);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // Chooses a file and presses "Upload file". It does not wait for the result: the caller
    // asserts what should appear (a new row, or the error), and that assertion is the wait.
    private async Task UploadAsync(string fileName)
    {
        await Page.Locator("#fileUpload").SetInputFilesAsync(new FilePayload
        {
            Name = fileName,
            MimeType = "application/pdf",
            Buffer = OnePagePdf()
        });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload file", Exact = true }).ClickAsync();
    }

    // WhatToChange -> Remove -> pupil search -> "Admitted following permanent exclusion" ->
    // DfE number and date -> the evidence page.
    private async Task NavigateToEvidencePageAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{SeededWindowId}");
        await Page.GetByLabel("Remove").First.CheckAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/pupil-search/**");

        var searchInput = Page.Locator("#pupil-search").First;
        await Expect(searchInput).ToBeVisibleAsync();
        await searchInput.FillAsync(PupilSurname);
        var pupil = Page.Locator("li[role='option']").GetByText($"{PupilSurname}, {PupilFirstName}");
        await Expect(pupil).ToBeVisibleAsync();
        await pupil.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/reason");

        await Page.Locator("input[name='q_reason'][value='permanent-exclusion']").CheckAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/permanent-exclusion");

        await Page.Locator("#q_permanent_exclusion_dfe_number").FillAsync("1234567");
        await Page.Locator("#q_date_pupil_excluded_day").FillAsync("1");
        await Page.Locator("#q_date_pupil_excluded_month").FillAsync("9");
        await Page.Locator("#q_date_pupil_excluded_year").FillAsync("2025");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Provide evidence for the removal of Alice Smith");
        await Expect(Page.Locator("#fileUpload")).ToBeVisibleAsync();
    }

    // The smallest valid one-page PDF, built here because this project has no PDF library and
    // takes no reference on the app. Pure ASCII, so a character offset is a byte offset and the
    // cross-reference table can be computed as the text is written. Checked against the app's
    // own parser (UglyToad.PdfPig) on 6 Oct 2026: 1 page.
    private static byte[] OnePagePdf()
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>"
        ];

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
