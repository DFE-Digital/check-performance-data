using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// #319: an admin can build a window that runs two checking exercises on different date ranges,
// which previously had to be done by hand in the database.
//
// The acceptance criteria this walks:
//   * two or more exercises, each on its own dates;
//   * the outer pair is derived, not typed — the wizard has no window-level date step at all, and
//     the summary shows the union;
//   * a single-exercise window is no harder than before (one date page instead of the two the old
//     window-level start/end steps took).
[Collection("E2E")]
public sealed class WindowExerciseWizardTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    public override BrowserNewContextOptions ContextOptions() =>
        new() { ViewportSize = new ViewportSize { Width = 1440, Height = 900 } };

    [SkippableFact]
    public async Task An_admin_can_create_a_window_that_runs_two_exercises_on_different_dates()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            AttachCookieToContext(await AuthHelpers.ImpersonateAsAdminAsync(Fixture));

            string title = $"E2E 16 to 19 {Guid.NewGuid():N}"[..24];

            await StartWizardAsync(title, windowType: "Post16", keyStage: "Post16");

            // Post16 pre-ticks both exercises, so Continue accepts them as they stand.
            await Expect(Page.Locator("h1")).ToContainTextAsync("Which checking exercises");
            await Expect(Page.Locator("input[name='Selected'][value='PupilData']")).ToBeCheckedAsync();
            await Expect(Page.Locator("input[name='Selected'][value='ResultsEnquiry']")).ToBeCheckedAsync();
            await Page.ClickAsync("button[type='submit']");

            // Pupil data checking runs for a fortnight...
            DateTime start = DateTime.UtcNow.AddMonths(2).Date;
            await Expect(Page.Locator("h1")).ToContainTextAsync("Pupil data checking dates");
            await FillDatesAsync(start, start.AddDays(14));

            // ...and results enquiry carries on for months after it, inside the same window.
            DateTime enquiryEnd = start.AddMonths(5);
            await Expect(Page.Locator("h1")).ToContainTextAsync("Results enquiry dates");
            await FillDatesAsync(start, enquiryEnd);

            // Check answers, then create.
            await Expect(Page.Locator("h1")).ToContainTextAsync("Check your answers");
            string checkAnswers = await Page.Locator("body").InnerTextAsync();
            Assert.Contains("Pupil data checking dates", checkAnswers);
            Assert.Contains("Results enquiry dates", checkAnswers);
            await Page.ClickAsync("button[type='submit']");

            // The summary derives the window's own dates as the union of the two exercises.
            await Expect(Page.Locator("h1")).ToContainTextAsync(title);
            string summary = await Page.Locator("body").InnerTextAsync();

            Assert.Contains("(earliest exercise start)", summary);
            Assert.Contains("(latest exercise end)", summary);
            Assert.Contains(enquiryEnd.ToString("dd/MM/yyyy"), summary);
            Assert.Contains("Pupil data checking, Results enquiry", summary);

            // Each exercise validates on its own, so each gets its own section and its own state.
            Assert.Contains("Validated", summary);
            Assert.True(
                await Page.Locator("h2:text('Pupil data checking')").CountAsync() > 0
                && await Page.Locator("h2:text('Results enquiry')").CountAsync() > 0,
                "Expected a per-exercise section for each of the window's two exercises.");
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    [SkippableFact]
    public async Task A_single_exercise_window_asks_for_one_set_of_dates()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            AttachCookieToContext(await AuthHelpers.ImpersonateAsAdminAsync(Fixture));

            string title = $"E2E KS4 {Guid.NewGuid():N}"[..20];

            await StartWizardAsync(title, windowType: "KS4June", keyStage: "KS4");

            // KS4 June pre-ticks pupil data checking only.
            await Expect(Page.Locator("input[name='Selected'][value='PupilData']")).ToBeCheckedAsync();
            await Expect(Page.Locator("input[name='Selected'][value='ResultsEnquiry']")).Not.ToBeCheckedAsync();
            await Page.ClickAsync("button[type='submit']");

            DateTime start = DateTime.UtcNow.AddMonths(2).Date;
            await Expect(Page.Locator("h1")).ToContainTextAsync("Pupil data checking dates");
            await FillDatesAsync(start, start.AddDays(14));

            // Straight to check answers — one exercise means one date page, not two.
            await Expect(Page.Locator("h1")).ToContainTextAsync("Check your answers");
            await Page.ClickAsync("button[type='submit']");

            await Expect(Page.Locator("h1")).ToContainTextAsync(title);
            string summary = await Page.Locator("body").InnerTextAsync();
            Assert.Contains("Pupil data checking", summary);
            Assert.DoesNotContain("Results enquiry", summary);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    // Copied from the sibling admin tests: the impersonation cookie has to be pushed onto the
    // Playwright context, because the helper authenticates over HttpClient.
    private void AttachCookieToContext(string? cookieHeader)
    {
        if (string.IsNullOrEmpty(cookieHeader)) return;
        var equalsIndex = cookieHeader.IndexOf('=');
        if (equalsIndex <= 0) return;

        Context.AddCookiesAsync([new Cookie
        {
            Name = cookieHeader[..equalsIndex],
            Value = cookieHeader[(equalsIndex + 1)..],
            Url = Fixture.BaseUrl
        }]).GetAwaiter().GetResult();
    }

    // Title, then window type, then key stage — the exercise step comes after the type because the
    // type decides which exercises start ticked.
    private async Task StartWizardAsync(string title, string windowType, string keyStage)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/windows/title");
        await Page.FillAsync("input[name='Title']", title);
        await Page.ClickAsync("button[type='submit']");

        await Page.CheckAsync($"input[name='WindowType'][value='{windowType}']");
        await Page.ClickAsync("button[type='submit']");

        await Page.CheckAsync($"input[name='KeyStage'][value='{keyStage}']");
        await Page.ClickAsync("button[type='submit']");
    }

    // Both ends of one exercise's range live on a single page.
    private async Task FillDatesAsync(DateTime start, DateTime end)
    {
        await Page.FillAsync("input[name='StartDate.Day']", start.Day.ToString());
        await Page.FillAsync("input[name='StartDate.Month']", start.Month.ToString());
        await Page.FillAsync("input[name='StartDate.Year']", start.Year.ToString());
        await Page.FillAsync("input[name='EndDate.Day']", end.Day.ToString());
        await Page.FillAsync("input[name='EndDate.Month']", end.Month.ToString());
        await Page.FillAsync("input[name='EndDate.Year']", end.Year.ToString());
        await Page.ClickAsync("button[type='submit']");
    }
}
