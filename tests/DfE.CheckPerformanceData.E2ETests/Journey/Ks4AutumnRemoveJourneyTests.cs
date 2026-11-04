using System.Text;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using DfE.CheckPerformanceData.E2ETests.Retrying;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// Issue #748 / AB#312039: the KS4 Autumn "Remove a pupil from data" journey. It follows the KS4
// June remove journey and offers one reason, "Pupil not on June list"; evidence is optional and
// every request goes to scrutiny (the decision itself is pinned by the rules-engine integration
// tests, not the browser).
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class Ks4AutumnRemoveJourneyTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded KS4Autumn window (DevDataSeeder.KeyStage4AutumnCheckingWindowId). Its pupil data
    // exercise is filled with the generated KS4 fixture pupils (Alice Smith is included) and run
    // through ingress by SeedExerciseFixtures, like the KS4 June fixture window.
    private static readonly Guid SeededWindowId = Guid.Parse("7D2E9A14-5C38-4B6F-A0E1-3F8B6C2D4A97");

    private const string PupilSurname = "Smith";
    private const string PupilFirstName = "Alice";

    // ── US1: the option appears and the journey runs ────────────────────────

    [RetryFact]
    [Trait("Category", "Smoke")]
    public async Task WhatToChange_OffersRemove()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{SeededWindowId}");

        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Remove']"))
            .ToHaveCountAsync(1);
    }

    [RetryFact]
    public async Task ReasonPage_OffersExactlyPupilNotOnJuneList()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToReasonPageAsync();

        // One option, and it is the issue's reason. No other window type's remove journey offers
        // it (spec FR-011), and this journey offers no other reason (spec assumption).
        Assert.Equal(1, await Page.Locator("input[name='q_reason']").CountAsync());
        await Expect(Page.GetByText("Pupil not on June list", new() { Exact = true })).ToBeVisibleAsync();

        await AxeChecks.ExpectNoViolationsAsync(Page, "KS4 Autumn remove reason page");
    }

    [RetryFact]
    [Trait("Category", "Smoke")]
    public async Task HappyPath_NotOnJuneList_SubmitsAndShowsAReference()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToReasonPageAsync();

        // Select by option value, not label text, so copy edits can't break this.
        await Page.Locator("input[name='q_reason'][value='not-on-june-list']").CheckAsync();
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/evidence");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Provide evidence for the removal of Alice Smith");
        await AxeChecks.ExpectNoViolationsAsync(Page, "KS4 Autumn remove evidence page");

        // Evidence is optional: continue with neither a file nor an explanation.
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/summary");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Summary of amendment request");
        var summary = await Page.Locator(".govuk-summary-list").InnerTextAsync();
        Assert.Contains("Pupil not on June list", summary);
        await AxeChecks.ExpectNoViolationsAsync(Page, "KS4 Autumn remove summary page");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/confirmation");
        await Expect(Page.Locator(".govuk-panel")).ToBeVisibleAsync();
        var panel = await Page.Locator(".govuk-panel").InnerTextAsync();
        var match = System.Text.RegularExpressions.Regex.Match(panel, @"CYPMD_KS4Autumn_[0-9A-F]{7}");
        Assert.True(match.Success, $"No reference number in the confirmation panel: {panel}");
    }

    // ── US2: only included pupils can be selected ───────────────────────────

    [RetryFact]
    [Trait("Category", "Smoke")]
    public async Task PupilSearch_OffersIncludedPupil_AndNotIncludedPupilIsNotOffered()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToPupilSearchAsync();

        var searchInput = Page.Locator("#pupil-search").First;

        // "Smith" is an included surname bucket in the fixture data: Alice Smith is offered and
        // selectable.
        await searchInput.FillAsync(PupilSurname);
        await Expect(Page.Locator("li[role='option']").GetByText($"{PupilSurname}, {PupilFirstName}"))
            .ToBeVisibleAsync();

        // "Johnson" exists only in the NOT-included bucket of the fixture data, so the journey's
        // INCLUDED filter must not offer it: the suggestions render their empty state.
        await searchInput.FillAsync("Johnson");
        await Expect(Page.Locator(".autocomplete__option--no-results"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.Equal(0, await Page.Locator("li[role='option']").CountAsync());
    }

    [RetryFact]
    public async Task PupilSearch_NoIncludedMatch_CannotContinueToReason()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToPupilSearchAsync();

        // Type a not-included-only name, dismiss the empty suggestion list, and continue: the
        // existing no-selection validation applies (the Remove journey has no decision pages).
        var searchInput = Page.Locator("#pupil-search").First;
        await searchInput.FillAsync("Johnson");
        await Expect(Page.Locator(".autocomplete__option--no-results"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await searchInput.PressAsync("Escape");
        await ContinueAsync();

        var summary = Page.Locator(".govuk-error-summary");
        await Expect(summary).ToBeVisibleAsync();
        Assert.Contains("Enter the name of the pupil", await summary.InnerTextAsync());
        Assert.Contains("/pupil-search/", Page.Url);
        Assert.DoesNotContain("/page/reason", Page.Url);
    }

    // ── US3: evidence is optional ───────────────────────────────────────────

    [RetryFact]
    [Trait("Category", "Smoke")]
    public async Task EvidencePage_MarksBothQuestionsOptional_AndContinuesWithNeither()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToEvidencePageAsync();

        // The established "(optional)" marker (lowercase, leading space) on both questions.
        await Expect(Page.GetByText("Upload files (optional)", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText(
            "How does the evidence demonstrate why the pupil should be removed? (optional)",
            new() { Exact = true })).ToBeVisibleAsync();

        // Neither field filled: the journey continues to the review screen, zero validation errors.
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/summary");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Summary of amendment request");
        Assert.Equal(0, await Page.Locator(".govuk-error-summary").CountAsync());
    }

    [RetryFact]
    public async Task Evidence_OnlyAnExplanation_IsCarriedToReview()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToEvidencePageAsync();

        await Page.Locator("#q_how_evidence_supports")
            .FillAsync("Pupil was not on the school's KS4 June list.");
        await ContinueAsync();

        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/summary");
        var summary = await Page.Locator(".govuk-summary-list").InnerTextAsync();
        Assert.Contains("Pupil was not on the school's KS4 June list.", summary);
    }

    [RetryFact]
    public async Task Evidence_OnlyAFile_IsCarriedToReview()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToEvidencePageAsync();

        await Page.Locator("input[name='fileUpload']").SetInputFilesAsync(new FilePayload
        {
            Name = "evidence.pdf",
            MimeType = "application/pdf",
            Buffer = OnePagePdf()
        });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload file", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "evidence.pdf", Exact = true }))
            .ToBeVisibleAsync();
        await ContinueAsync();

        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/summary");
        // Uploaded evidence is not in the answers summary list: it is the "Summary of uploaded
        // documents" table, a download link named by the original file name.
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "evidence.pdf", Exact = true }))
            .ToBeVisibleAsync();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // WhatToChange -> Remove -> pupil search.
    private async Task NavigateToPupilSearchAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{SeededWindowId}");
        await Page.GetByLabel("Remove").First.CheckAsync();
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/pupil-search/**");
    }

    private async Task SearchAndSelectPupilAsync(string surname, string firstName)
    {
        var searchInput = Page.Locator("#pupil-search").First;
        await Expect(searchInput).ToBeVisibleAsync();
        await searchInput.FillAsync(surname);
        var pupil = Page.Locator("li[role='option']").GetByText($"{surname}, {firstName}");
        await Expect(pupil).ToBeVisibleAsync();
        await pupil.ClickAsync();
        await ContinueAsync();
    }

    // WhatToChange -> Remove -> pupil search -> select the seeded pupil -> reason page.
    private async Task NavigateToReasonPageAsync()
    {
        await NavigateToPupilSearchAsync();
        await SearchAndSelectPupilAsync(PupilSurname, PupilFirstName);
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/reason");
    }

    // WhatToChange -> Remove -> pupil search -> select the pupil -> reason -> the one reason.
    private async Task NavigateToEvidencePageAsync()
    {
        await NavigateToReasonPageAsync();
        await Page.Locator("input[name='q_reason'][value='not-on-june-list']").CheckAsync();
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/evidence");
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

    // The smallest valid one-page PDF, duplicated from EvidenceUploadFileLimitTests: this
    // project has no PDF library and takes no reference on the app.
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
