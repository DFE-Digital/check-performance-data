using System.Text;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using DfE.CheckPerformanceData.E2ETests.Retrying;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// GitHub #380 (Zoonou, WCAG 4.1.2): Dragon NaturallySpeaking could not reach the Provide
// Evidence file input by voice — a native <input type="file"> cannot be activated by Dragon in
// Chrome. The page now opts into GOV.UK Frontend's JavaScript-enhanced File upload, which puts
// a real <button> in its place. Dragon targets a control by role and accessible name, so the
// contract this class pins is: a button, named by the label, that opens the file picker when
// activated. Nothing here drives Dragon itself; the retest is Zoonou's.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class EvidenceUploadEnhancedControlTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // Same seeded KS4June window and pupil as EvidenceUploadFileLimitTests.
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");
    private const string PupilSurname = "Smith";
    private const string PupilFirstName = "Alice";

    private ILocator ChooseFileButton => Page.Locator("button#fileUpload");
    private ILocator NativeInput => Page.Locator("input[name='fileUpload']");

    [RetryFact]
    public async Task TheChooseFileControl_IsARealButton_NamedByTheLabel_AndTheNativeInputIsHidden()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToEvidencePageAsync();

        // The id moved to the button, so the error summary's #fileUpload link lands on it.
        await Expect(ChooseFileButton).ToBeVisibleAsync();
        await Expect(ChooseFileButton).ToHaveAttributeAsync("type", "button");
        // Accessible name = label + status + button text (aria-labelledby). Dragon's "click
        // Choose file" matches on this name.
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^Upload a file.*Choose file") }))
            .ToBeVisibleAsync();
        await Expect(ChooseFileButton).ToContainTextAsync("No file chosen");

        // The native input survives, renamed and hidden, still carrying the form field name.
        await Expect(NativeInput).ToHaveAttributeAsync("id", "fileUpload-input");
        await Expect(NativeInput).ToBeHiddenAsync();
        await Expect(NativeInput).ToHaveAttributeAsync("accept", ".pdf,application/pdf");

        // #389 / AB#304900 still hold on the control the user reaches: the description the
        // module copied names the count line and never the hint.
        await Expect(ChooseFileButton).ToHaveAttributeAsync("aria-describedby", new Regex(@"\bfileUpload-count\b"));
        await Expect(ChooseFileButton).Not.ToHaveAttributeAsync("aria-describedby", new Regex(@"\bfileUpload-hint\b"));
    }

    [RetryFact]
    public async Task ActivatingTheButton_OpensTheFilePicker_ByClickAndByKeyboard()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToEvidencePageAsync();

        // A voice "click" is a click: the button forwards it to the hidden input.
        var byClick = await Page.RunAndWaitForFileChooserAsync(async () => await ChooseFileButton.ClickAsync());
        Assert.False(byClick.IsMultiple);
        await byClick.SetFilesAsync(new FilePayload { Name = "evidence-1.pdf", MimeType = "application/pdf", Buffer = OnePagePdf() });
        await Expect(ChooseFileButton).ToContainTextAsync("evidence-1.pdf");

        // Keyboard users reach the same button and activate it with Enter.
        await ChooseFileButton.FocusAsync();
        var byKeyboard = await Page.RunAndWaitForFileChooserAsync(async () => await Page.Keyboard.PressAsync("Enter"));
        await byKeyboard.SetFilesAsync(new FilePayload { Name = "evidence-2.pdf", MimeType = "application/pdf", Buffer = OnePagePdf() });
        await Expect(ChooseFileButton).ToContainTextAsync("evidence-2.pdf");

        // The chosen file still uploads through the unchanged form post.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload file", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "evidence-2.pdf", Exact = true })).ToBeVisibleAsync();
        await Expect(ChooseFileButton).ToContainTextAsync("No file chosen");
    }

    [RetryFact]
    public async Task ADuplicateFileName_IsWarnedAboveTheButton_AndNamedInItsDescription()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToEvidencePageAsync();

        await NativeInput.SetInputFilesAsync(new FilePayload { Name = "evidence-1.pdf", MimeType = "application/pdf", Buffer = OnePagePdf() });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Upload file", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "evidence-1.pdf", Exact = true })).ToBeVisibleAsync();

        // Choosing the same name again is warned at selection time (AB#296081), and the
        // warning must land where the GOV.UK error message sits — above the control, not
        // inside the wrapper under the button — and be named by the button's description.
        await NativeInput.SetInputFilesAsync(new FilePayload { Name = "evidence-1.pdf", MimeType = "application/pdf", Buffer = OnePagePdf() });
        var warning = Page.Locator("#fileUpload-duplicate-error");
        await Expect(warning).ToContainTextAsync("The file name has already been used.");
        await Expect(Page.Locator("#fileUpload-duplicate-error + .govuk-file-upload-wrapper")).ToHaveCountAsync(1);
        await Expect(ChooseFileButton).ToHaveAttributeAsync("aria-describedby", new Regex(@"\bfileUpload-duplicate-error\b"));
        await Expect(NativeInput).ToHaveAttributeAsync("aria-describedby", new Regex(@"\bfileUpload-duplicate-error\b"));

        // A different name clears it from both.
        await NativeInput.SetInputFilesAsync(new FilePayload { Name = "evidence-2.pdf", MimeType = "application/pdf", Buffer = OnePagePdf() });
        await Expect(warning).ToHaveCountAsync(0);
        await Expect(ChooseFileButton).Not.ToHaveAttributeAsync("aria-describedby", new Regex(@"\bfileUpload-duplicate-error\b"));
    }

    // WhatToChange -> Remove -> pupil search -> "Admitted following permanent exclusion" ->
    // DfE number and date -> the evidence page (the EvidenceUploadFileLimitTests route).
    private async Task NavigateToEvidencePageAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{SeededWindowId}");
        await Page.GetByLabel("Remove").First.CheckAsync();
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

        await Page.Locator("input[name='q_reason'][value='permanent-exclusion']").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/permanent-exclusion");

        await Page.Locator("#q_permanent_exclusion_dfe_number").FillAsync("1234567");
        await Page.Locator("#q_date_pupil_excluded_day").FillAsync("1");
        await Page.Locator("#q_date_pupil_excluded_month").FillAsync("9");
        await Page.Locator("#q_date_pupil_excluded_year").FillAsync("2025");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Provide evidence for the removal of Alice Smith");
        await Expect(ChooseFileButton).ToBeVisibleAsync();
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
