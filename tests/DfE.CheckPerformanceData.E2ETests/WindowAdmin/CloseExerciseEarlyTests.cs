using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.WindowAdmin;

/// <summary>
/// AB#301022: an admin closes an open checking exercise before its scheduled end. Walks the
/// acceptance criteria an admin can see: Close is offered while the exercise is open (AC1), the
/// confirmation names the window and its scheduled end and says it cannot be undone (AC3), a wrong
/// name leaves it open with a reason (AC4), the exact name closes it (AC5), a closed exercise
/// offers no Close and refuses the URL (AC2), and the audit log records it (AC7).
/// AC6 — schools can no longer act — is pinned below the browser, by the gates' own unit tests,
/// JourneyControllerTests' refresh facts and WindowRepositoryEarlyClosureTests.
/// </summary>
/// <remarks>
/// The window is built here, through the wizard, and is never a seeded one: closing also sends the
/// exercise's submitted requests for processing and cancels its drafts, which on a seeded window
/// would pull the data out from under every other class in the collection.
/// </remarks>
[Collection("E2E")]
public sealed class CloseExerciseEarlyTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string CloseButton = "Close Pupil data checking";
    private const string SendButton = "Send requests for processing for Pupil data checking";

    // The summary and close pages are gated on the manage-window section, so the collection's
    // default editor principal 404s on them. Switch to admin for this class and hand the
    // collection's editor cookie back afterwards, as NextOpportunityTests does.
    public override async Task InitializeAsync()
    {
        await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
    }

    [Fact]
    public async Task An_admin_closes_an_open_exercise_early_by_typing_the_window_name()
    {
        string title = $"E2E Close {Guid.NewGuid():N}"[..22];
        string windowId = await CreateOpenKs4WindowAsync(title);
        string summaryUrl = $"{Fixture.BaseUrl}/admin/windows/summary/{windowId}";

        // AC1: an open exercise offers Close — and not the hand-over on its own. Close is
        // addressed by the exercise's id (#466), read from the button itself.
        await Expect(Button(SendButton)).ToHaveCountAsync(0);
        string closeHref = (await Button(CloseButton).GetAttributeAsync("href"))!;
        Assert.Matches($"^/admin/windows/{windowId}/exercises/[0-9a-fA-F-]{{36}}/close$", closeHref);
        string closeUrl = $"{Fixture.BaseUrl}{closeHref}";
        await Button(CloseButton).ClickAsync();

        // AC3: the window name, the scheduled end, and that it cannot be undone.
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Close Pupil data checking early?");
        string confirmation = await Page.Locator("main").InnerTextAsync();
        Assert.Contains(title, confirmation);
        Assert.Contains("Scheduled end", confirmation);
        Assert.Contains("This exercise is due to close on", confirmation);
        Assert.Contains("You cannot undo this.", confirmation);
        await Expect(Page.Locator("#confirm-window-name-hint")).ToHaveTextAsync($"Enter exactly: {title}");

        // AC4: a wrong name leaves it open and says why, once.
        await Page.FillAsync("#confirm-window-name", "not the window name");
        await Button(CloseButton).ClickAsync();
        await Expect(Page.Locator(".govuk-error-summary")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".govuk-error-summary"))
            .ToContainTextAsync("The window name you entered does not match");
        await Expect(Page.Locator("#confirm-window-name-error"))
            .ToContainTextAsync("The window name you entered does not match");
        await Expect(Page.Locator("#confirm-window-name")).ToHaveValueAsync("not the window name");

        await Page.GotoAsync(summaryUrl);
        await Expect(Button(CloseButton)).ToBeVisibleAsync();

        // AC5: the exact name closes it.
        await Page.GotoAsync(closeUrl);
        await Page.FillAsync("#confirm-window-name", title);
        await Button(CloseButton).ClickAsync();
        await Page.WaitForURLAsync($"**/admin/windows/summary/{windowId}");
        await Expect(Page.Locator(".govuk-notification-banner--success"))
            .ToContainTextAsync("Pupil data checking was closed early on");

        // AC2: a closed exercise offers no Close — only the hand-over — and the URL is refused.
        await Expect(Button(CloseButton)).ToHaveCountAsync(0);
        await Expect(Button(SendButton)).ToBeVisibleAsync();

        await Page.GotoAsync(closeUrl);
        await Page.WaitForURLAsync($"**/admin/windows/summary/{windowId}");
        await Expect(Page.Locator(".govuk-notification-banner"))
            .ToContainTextAsync("Pupil data checking is not open, so it was not closed.");

        // AC7: who, which window and exercise, when, and that it was early.
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/audit-log?activity=WindowAdmin&windowId={windowId}");
        var row = Page.Locator(
            $"tr[data-entity-type='WindowAdmin'][data-entity-id='{windowId}'][data-action='ClosedEarly']");
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row).ToContainTextAsync("Window admin");
        await Expect(row).ToContainTextAsync(title);
        await Expect(row).ToContainTextAsync("Pupil data checking closed early, before scheduled end");
        await Expect(row).ToContainTextAsync("Success");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // govuk-button-link renders <a role="button">, and the confirmation's submit is a <button>;
    // both answer to the button role.
    private ILocator Button(string name) =>
        Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    // A KS4 June window whose only exercise, pupil data checking, opened today and runs for a
    // fortnight — so it is open now. Returns the new window's id, read from the summary URL.
    private async Task<string> CreateOpenKs4WindowAsync(string title)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/windows/title");
        await Page.FillAsync("input[name='Title']", title);
        await Page.ClickAsync("button[type='submit']");

        await Page.CheckAsync("input[name='WindowType'][value='KS4June']");
        await Page.ClickAsync("button[type='submit']");

        // KS4 June pre-ticks pupil data checking only.
        await Expect(Page.Locator("input[name='Selected'][value='PupilData']")).ToBeCheckedAsync();
        await Page.ClickAsync("button[type='submit']");

        // The wizard rejects a start date before today (UTC), so today is the earliest it takes.
        DateTime start = DateTime.UtcNow.Date;
        DateTime end = start.AddDays(14);
        await Expect(Page.Locator("h1")).ToContainTextAsync("Pupil data checking dates");
        await Page.FillAsync("input[name='StartDate.Day']", start.Day.ToString());
        await Page.FillAsync("input[name='StartDate.Month']", start.Month.ToString());
        await Page.FillAsync("input[name='StartDate.Year']", start.Year.ToString());
        await Page.FillAsync("input[name='EndDate.Day']", end.Day.ToString());
        await Page.FillAsync("input[name='EndDate.Month']", end.Month.ToString());
        await Page.FillAsync("input[name='EndDate.Year']", end.Year.ToString());
        await Page.ClickAsync("button[type='submit']");

        await Expect(Page.Locator("h1")).ToContainTextAsync("Check your answers");
        await Page.ClickAsync("button[type='submit']");

        await Expect(Page.Locator("h1")).ToContainTextAsync(title);
        var match = Regex.Match(Page.Url, "/admin/windows/summary/([0-9a-fA-F-]{36})");
        Assert.True(match.Success, $"Expected the new window's summary URL, got {Page.Url}");
        return match.Groups[1].Value.ToLowerInvariant();
    }
}
