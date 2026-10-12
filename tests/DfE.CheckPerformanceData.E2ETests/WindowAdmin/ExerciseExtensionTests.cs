using System.Globalization;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.WindowAdmin;

/// <summary>
/// #567 / AB#307773: an admin gives named schools more time on a checking exercise. Walks what an
/// admin can see:
/// <list type="bullet">
/// <item>Smoke — adding one: a line that is not a LAESTAB is refused with its line number, a
/// school with no data file is warned of and saved only once confirmed, the exercise's card lists
/// the extension as Open with Edit and Close early, and the audit log records it.</item>
/// <item>Editing one (a school removed), closing the exercise early (its page says the extension's
/// schools keep their time), closing the extension early (a wrong window name refused, the exact
/// name accepted), and sending its requests once both have ended — each with its banner and audit
/// row.</item>
/// <item>A display-only data share has no time to extend, so its card offers no Add extension
/// while a kind-bearing card on the same window does.</item>
/// </list>
/// What the extension does for a school — the journey's gates and deadlines — is pinned below the
/// browser, by the gate and resolver unit tests and the repository integration tests.
/// </summary>
/// <remarks>
/// The add/edit/close/send facts build their own window through the wizard, as
/// CloseExerciseEarlyTests does: sending an extension's requests sweeps its schools' requests, and
/// on a seeded window that would pull data out from under every other class in the collection.
/// That window has no release yet, so every school on it is warned of as having no data file. The
/// data-share fact only reads a seeded window's summary page.
/// </remarks>
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class ExerciseExtensionTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string Exercise = "Pupil data checking";
    private const string AddExtensionButton = $"Add extension to {Exercise}";

    // The dev seed's 16-19 October window: pupil data checking and results enquiry beside the
    // display-only "Summary" data share (SeedCheckingWindows.WithSummaryDataShare).
    private const string Post16OctoberWindowId = "ec6493b8-9b66-4090-8be6-9dfc3805751f";

    // The summary and extension pages are gated on the manage-window section, so the collection's
    // default editor principal 404s on them. Switch to admin for this class and hand the
    // collection's editor cookie back afterwards, as CloseExerciseEarlyTests does.
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
    [Trait("Category", "Smoke")]
    public async Task An_admin_adds_an_extension_and_sees_it_on_the_summary()
    {
        string title = $"E2E Ext {Guid.NewGuid():N}"[..20];
        string windowId = await CreateOpenKs4WindowAsync(title);

        string ending = await AddTwoSchoolExtensionAsync(windowId);

        // The card lists it: one row, open, editable and closable — and nothing to send yet.
        var table = ExtensionsTable();
        await Expect(table.Locator("tbody tr")).ToHaveCountAsync(1);
        var row = table.Locator("tbody tr");
        await Expect(row.Locator("th")).ToHaveTextAsync(ending);
        await Expect(row.Locator("td").Nth(0)).ToHaveTextAsync("2");
        await Expect(row.Locator(".govuk-tag")).ToHaveTextAsync("Open");
        await Expect(Link($"Edit extension ending {ending}")).ToBeVisibleAsync();
        await Expect(Link($"Close early extension ending {ending}")).ToBeVisibleAsync();
        await Expect(Link($"Send requests for processing for extension ending {ending}")).ToHaveCountAsync(0);

        // The audit log records who added it, to which exercise, for how many schools.
        await Page.GotoAsync(AuditLogUrl(windowId));
        var audit = AuditRow(windowId, "ExtensionAdded");
        await Expect(audit).ToHaveCountAsync(1);
        await Expect(audit).ToContainTextAsync(title);
        await Expect(audit).ToContainTextAsync($"{Exercise} extension added for 2 schools");
        await Expect(audit).ToContainTextAsync("Success");
    }

    [Fact]
    public async Task An_admin_edits_closes_early_and_sends_requests_for_an_extension()
    {
        string title = $"E2E Ext {Guid.NewGuid():N}"[..20];
        string windowId = await CreateOpenKs4WindowAsync(title);
        string summaryUrl = $"{Fixture.BaseUrl}/admin/windows/summary/{windowId}";

        string ending = await AddTwoSchoolExtensionAsync(windowId);

        // Edit: the form comes back filled in; dropping a school saves the shorter list. The one
        // left still has no data file, so the warning asks again, worded for a save.
        await Link($"Edit extension ending {ending}").ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Edit extension");
        await Expect(Page.Locator("#Schools")).ToHaveValueAsync(new Regex("860/4070"));
        await Page.FillAsync("#Schools", "860/4070");
        await Button("Save extension").ClickAsync();
        await Expect(Page.GetByLabel("Save these schools anyway")).ToBeVisibleAsync();
        await Page.GetByLabel("Save these schools anyway").CheckAsync();
        await Button("Save extension").ClickAsync();
        await Page.WaitForURLAsync($"**/admin/windows/summary/{windowId}");
        await Expect(SuccessBanner()).ToContainTextAsync($"Extension updated for 1 school, ending {ending}.");
        await Expect(ExtensionsTable().Locator("tbody tr td").Nth(0)).ToHaveTextAsync("1");

        // Close the EXERCISE early first (CloseExerciseEarlyTests' typed-name flow). Send waits for
        // the later of the two ends, so without this the extension closed early below would wait
        // for the exercise's own end and offer no Send. The page says the open extension's schools
        // keep their time, and the extension stays open on the card.
        await Button($"Close {Exercise}").ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync($"Close {Exercise} early?");
        await Expect(Page.Locator("main")).ToContainTextAsync("Schools in an open extension keep their extra time.");
        await Page.FillAsync("#confirm-window-name", title);
        await Button($"Close {Exercise}").ClickAsync();
        await Page.WaitForURLAsync($"**/admin/windows/summary/{windowId}");
        await Expect(SuccessBanner()).ToContainTextAsync($"{Exercise} was closed early on");
        await Expect(ExtensionsTable().Locator(".govuk-tag")).ToHaveTextAsync("Open");

        // Close early: a wrong window name leaves it open and says why, once.
        await Link($"Close early extension ending {ending}").ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync($"Close the {Exercise} extension early?");
        string closeUrl = Page.Url;
        await Page.FillAsync("#confirm-window-name", "not the window name");
        await Button("Close extension").ClickAsync();
        await Expect(Page.Locator(".govuk-error-summary")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".govuk-error-summary"))
            .ToContainTextAsync("The window name you entered does not match");
        await Expect(Page.Locator("#confirm-window-name-error"))
            .ToContainTextAsync("The window name you entered does not match");

        await Page.GotoAsync(summaryUrl);
        await Expect(ExtensionsTable().Locator(".govuk-tag")).ToHaveTextAsync("Open");

        // The exact name closes it, and the banner gives its scheduled end and when it closed.
        await Page.GotoAsync(closeUrl);
        await Page.FillAsync("#confirm-window-name", title);
        await Button("Close extension").ClickAsync();
        await Page.WaitForURLAsync($"**/admin/windows/summary/{windowId}");
        string scheduled = ending.Replace(" ", ", ");
        await Expect(SuccessBanner())
            .ToContainTextAsync($"Extension of {Exercise} ending {scheduled} was closed early on");

        // #535: the closing time is UK time whatever zone the container runs in.
        string banner = await SuccessBanner().InnerTextAsync();
        Match closedOn = Regex.Match(banner, @"closed early on (\d{2}/\d{2}/\d{4}, \d{2}:\d{2})");
        Assert.True(closedOn.Success, banner);
        DateTime shown = DateTime.ParseExact(
            closedOn.Groups[1].Value, "dd/MM/yyyy, HH:mm", CultureInfo.InvariantCulture);
        DateTime ukNow = UkNow();
        Assert.InRange(shown, ukNow.AddMinutes(-5), ukNow.AddMinutes(1));

        // Closing early may move the end it shows, so the one row is read, not named by its end: it
        // is closed, and — the exercise having ended too — its only action is the hand-over.
        var row = ExtensionsTable().Locator("tbody tr");
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row.Locator(".govuk-tag")).ToHaveTextAsync("Closed");
        await Expect(row.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = false })).ToHaveCountAsync(0);
        await Expect(row.GetByRole(AriaRole.Link, new() { Name = "Close early", Exact = false })).ToHaveCountAsync(0);
        var send = row.GetByRole(AriaRole.Link,
            new() { NameRegex = new Regex("^Send requests for processing for extension ending ") });
        await Expect(send).ToHaveCountAsync(1);

        // Send requests: a confirmation first, then the counts. A new window has no requests.
        await send.ClickAsync();
        await Expect(Page.Locator("h1"))
            .ToHaveTextAsync($"Send the {Exercise} extension's requests for processing");
        await Button("Send requests for processing").ClickAsync();
        await Page.WaitForURLAsync($"**/admin/windows/summary/{windowId}");
        await Expect(SuccessBanner()).ToContainTextAsync(
            $"0 requests sent for processing and 0 drafts cancelled for the extension of {Exercise}.");

        // Sent is shown beside Closed; the link stays, as re-sending is harmless.
        await Expect(ExtensionsTable().Locator("tbody tr .govuk-tag")).ToHaveTextAsync(["Closed", "Requests sent"]);
        await Expect(ExtensionsTable().GetByRole(AriaRole.Link,
            new() { NameRegex = new Regex("^Send requests for processing for extension ending ") })).ToHaveCountAsync(1);

        // Each step is in the audit log, once.
        await Page.GotoAsync(AuditLogUrl(windowId));
        var edited = AuditRow(windowId, "ExtensionEdited");
        await Expect(edited).ToHaveCountAsync(1);
        await Expect(edited).ToContainTextAsync($"{Exercise} extension edited: 0 schools added, 1 school removed");
        var closed = AuditRow(windowId, "ExtensionClosedEarly");
        await Expect(closed).ToHaveCountAsync(1);
        await Expect(closed).ToContainTextAsync($"{Exercise} extension closed early, before its end");
        var sent = AuditRow(windowId, "ExtensionRequestsSent");
        await Expect(sent).ToHaveCountAsync(1);
        await Expect(sent).ToContainTextAsync(
            $"{Exercise} extension requests sent for processing: 0 requests sent, 0 drafts cancelled");
    }

    [Fact]
    public async Task A_data_share_offers_no_extension()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/windows/summary/{Post16OctoberWindowId}");

        var share = Card("Summary");
        var kindBearing = Card(Exercise);
        await Expect(share).ToHaveCountAsync(1);
        await Expect(kindBearing).ToHaveCountAsync(1);

        // A data share has no journey, so no time to extend: no button, and no extensions block.
        await Expect(share.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Add extension") }))
            .ToHaveCountAsync(0);
        await Expect(share.GetByText("No extensions.")).ToHaveCountAsync(0);
        await Expect(share.GetByRole(AriaRole.Table, new() { NameRegex = new Regex("^Extensions") }))
            .ToHaveCountAsync(0);

        await Expect(kindBearing.GetByRole(AriaRole.Button, new() { Name = AddExtensionButton, Exact = true }))
            .ToBeVisibleAsync();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // From the new window's summary: a bad line is refused, then two good schools — neither has a
    // data file on a window with no release — are saved once confirmed. Returns the extension's
    // end as the summary prints it (dd/MM/yyyy HH:mm).
    private async Task<string> AddTwoSchoolExtensionAsync(string windowId)
    {
        await Button(AddExtensionButton).ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync($"Add an extension to {Exercise}");

        // A week past the exercise's own end, at 17:00 UK time: read the end the page states,
        // rather than recomputing the wizard's, so a month boundary cannot shift it.
        string inset = await Page.Locator(".govuk-inset-text").InnerTextAsync();
        Match endsOn = Regex.Match(inset, @"ends on (\d{2}/\d{2}/\d{4}) \d{2}:\d{2}");
        Assert.True(endsOn.Success, inset);
        DateTime end = DateTime.ParseExact(endsOn.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture)
            .AddDays(7).AddHours(17);

        await Page.FillAsync("input[name='EndDate.Day']", end.Day.ToString(CultureInfo.InvariantCulture));
        await Page.FillAsync("input[name='EndDate.Month']", end.Month.ToString(CultureInfo.InvariantCulture));
        await Page.FillAsync("input[name='EndDate.Year']", end.Year.ToString(CultureInfo.InvariantCulture));
        await Page.FillAsync("input[name='EndHour']", "17");
        await Page.FillAsync("input[name='EndMinute']", "00");

        // A line that is not a LAESTAB is named by its line number, in the page's one summary.
        await Page.FillAsync("#Schools", "abc");
        await Button("Save extension").ClickAsync();
        await Expect(Page.Locator(".govuk-error-summary")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".govuk-error-summary")).ToContainTextAsync("Line 1: 'abc' is not a LAESTAB");
        await Expect(Page.Locator("#Schools-error")).ToContainTextAsync("Line 1: 'abc' is not a LAESTAB");

        // Two good schools, in both forms. Neither has a data file here, so nothing is saved until
        // the admin confirms; the warning lists them in slashed form.
        await Page.FillAsync("#Schools", "860/4070\n9334290");
        await Button("Save extension").ClickAsync();
        await Expect(Page.Locator("#ConfirmNoData-warning")).ToBeVisibleAsync();
        await Expect(Page.Locator("#ConfirmNoData-schools li")).ToHaveTextAsync(["860/4070", "933/4290"]);
        await Expect(Page.Locator(".govuk-error-summary"))
            .ToContainTextAsync("Confirm that you want to add these schools, or remove them from the list");

        await Page.GetByLabel("Add these schools anyway").CheckAsync();
        await Button("Save extension").ClickAsync();
        await Page.WaitForURLAsync($"**/admin/windows/summary/{windowId}");

        string ending = end.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        await Expect(SuccessBanner()).ToContainTextAsync($"Extension added for 2 schools, ending {ending}.");
        return ending;
    }

    private ILocator SuccessBanner() => Page.Locator(".govuk-notification-banner--success");

    // The card's Extensions table, named by its caption ("Extensions for {exercise}").
    private ILocator ExtensionsTable() =>
        Page.GetByRole(AriaRole.Table, new() { Name = $"Extensions for {Exercise}", Exact = true });

    // The summary card whose title starts with the exercise's name (the kind tag follows it).
    private ILocator Card(string exerciseName) =>
        Page.Locator(".govuk-summary-card").Filter(new()
        {
            Has = Page.Locator(".govuk-summary-card__title",
                new() { HasTextRegex = new Regex($"^\\s*{Regex.Escape(exerciseName)}\\s") })
        });

    private string AuditLogUrl(string windowId) =>
        $"{Fixture.BaseUrl}/admin/audit-log?activity=WindowAdmin&windowId={windowId}";

    private ILocator AuditRow(string windowId, string action) =>
        Page.Locator($"tr[data-entity-type='WindowAdmin'][data-entity-id='{windowId}'][data-action='{action}']");

    // govuk-button-link renders <a role="button">, and the forms' submits are <button>s; both
    // answer to the button role.
    private ILocator Button(string name) =>
        Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    // The extension table's actions are plain links; their names carry a visually hidden suffix.
    private ILocator Link(string name) =>
        Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });

    private static DateTime UkNow() =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime;

    // A KS4 June window whose only exercise, pupil data checking, opened today and runs for a
    // fortnight — so it is open now. Returns the new window's id, read from the summary URL.
    // The same walk as CloseExerciseEarlyTests' copy.
    private async Task<string> CreateOpenKs4WindowAsync(string title)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/windows/title");
        await Page.FillAsync("input[name='Title']", title);
        await Page.ClickAsync("button[type='submit']");

        // Since #466 the type step gives the draft its default exercises (KS4 June: pupil data
        // checking only), so the wizard goes straight to its dates.
        await Page.CheckAsync("input[name='WindowType'][value='KS4June']");
        await Page.ClickAsync("button[type='submit']");

        // The site rejects a start date before today in the UK (#535), so "today" is the UK date.
        DateTime start = UkNow().Date;
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
